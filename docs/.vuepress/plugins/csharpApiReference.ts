/**
 * VuePress plugin that turns Package.{version}.json files (produced by
 * docs/tools/PackageJsonGenerator) into a browsable C# API reference.
 *
 * For each JSON package it registers:
 *   - one landing page at `${basePath}/`
 *   - one page per type at  `${basePath}/${typeSlug}/`
 *
 * Pages are virtual (not on disk) — each is created via `createPage` with
 * generated Markdown that VuePress renders through its normal pipeline.
 */
import fs from "node:fs/promises";
import path from "node:path";
import type { App, Plugin, Page } from "vuepress/core";
import { createPage } from "vuepress/core";

interface PackageMeta {
    name: string;
    version: string;
    targetFramework?: string;
    sourceRepository?: string;
    sourceCommit?: string;
}

interface DocNode {
    kind: string;
    text?: string;
    value?: string;
    language?: string;
    children?: DocNode[];
    items?: { term?: DocNode[]; description: DocNode[] }[];
    header?: { term?: DocNode[]; description: DocNode[] };
    style?: string;
}

interface Parameter {
    name: string;
    type: string;
    isOptional?: boolean;
    isParams?: boolean;
    isOut?: boolean;
    isRef?: boolean;
    isIn?: boolean;
    defaultValue?: string;
}

interface Member {
    name: string;
    kind: string;
    signature?: string;
    parameters?: Parameter[];
    returnType?: string;
    type?: string; // properties / fields use `type`
    accessibility?: string;
    isStatic?: boolean;
    isAbstract?: boolean;
    isVirtual?: boolean;
    isOverride?: boolean;
    isSealed?: boolean;
    isReadOnly?: boolean;
    isAsync?: boolean;
    isExtension?: boolean;
    docs?: { summary?: DocNode[]; remarks?: DocNode[]; returns?: DocNode[]; value?: DocNode[]; parameters?: Record<string, DocNode[]>; };
}

interface Type {
    name: string;
    fullName: string;
    namespace?: string;
    kind: string;
    accessibility?: string;
    isAbstract?: boolean;
    isStatic?: boolean;
    isSealed?: boolean;
    isReadOnly?: boolean;
    isGeneric?: boolean;
    genericParameters?: { name: string }[];
    baseType?: string;
    interfaces?: string[];
    members?: Member[];
    enumMembers?: { name: string; value: number; description?: string }[];
    docs?: { summary?: DocNode[]; remarks?: DocNode[] };
}

interface PackageDoc {
    package: PackageMeta;
    types: Type[];
}

export interface CsharpApiReferenceOptions {
    /** Absolute path to the directory containing Package.{version}.json files. */
    dataDir: string;
    /** Base URL path, e.g. "/client/api". No trailing slash. */
    basePath: string;
}

const KIND_LABEL: Record<string, string> = {
    class: "Class",
    interface: "Interface",
    struct: "Struct",
    enum: "Enum",
    delegate: "Delegate",
    record: "Record",
    "record struct": "Record Struct",
};

const MEMBER_KIND_ORDER = ["constructor", "property", "method", "field", "event", "indexer"] as const;
const MEMBER_KIND_LABEL: Record<string, string> = {
    constructor: "Constructors",
    property: "Properties",
    method: "Methods",
    field: "Fields",
    event: "Events",
    indexer: "Indexers",
};

/* ── slug + display helpers ─────────────────────────────────────── */

function genericArity(t: { genericParameters?: { name: string }[] }) {
    return t.genericParameters?.length ?? 0;
}

function typeSlug(t: Type): string {
    const slug = t.name.toLowerCase();
    const arity = genericArity(t);
    return arity > 0 ? `${slug}-${arity}` : slug;
}

function typeDisplayName(t: Type): string {
    if (t.isGeneric && t.genericParameters?.length) {
        return `${t.name}<${t.genericParameters.map((g) => g.name).join(", ")}>`;
    }
    return t.name;
}

function shortTypeName(full: string): string {
    if (!full) return full;
    const noArity = full.replace(/`\d+/g, "");
    // Split outer name from generic args / parameter list so we only strip the outer namespace.
    const splitIdx = (() => {
        const lt = noArity.indexOf("<");
        const lp = noArity.indexOf("(");
        const candidates = [lt, lp].filter((i) => i >= 0);
        return candidates.length ? Math.min(...candidates) : -1;
    })();
    const outer = splitIdx >= 0 ? noArity.slice(0, splitIdx) : noArity;
    const tail = splitIdx >= 0 ? noArity.slice(splitIdx) : "";
    const lastDot = outer.lastIndexOf(".");
    const outerShort = lastDot >= 0 ? outer.slice(lastDot + 1) : outer;
    return outerShort + tail;
}

/** Look up a type in `allTypes` by full name, ignoring generic-arity markers. */
function findTypeByFullName(allTypes: Type[], fullName: string): Type | undefined {
    if (!fullName) return undefined;
    const stripped = fullName.replace(/`\d+/g, "");
    return allTypes.find((t) => t.fullName === stripped || t.fullName === fullName);
}

function memberAnchor(member: Member): string {
    const name = member.name === ".ctor" ? "constructor" : member.name;
    return name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

/* ── Markdown escaping ──────────────────────────────────────────── */

function escapeMd(text: string): string {
    // Escape characters that trip up markdown-it inline parsing, AND HTML-escape
    // angle brackets (Vue's parser would otherwise treat `<Foo>` text as a tag).
    return text
        .replace(/([\\`*_{}\[\]()#+\-.!|])/g, "\\$1")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;");
}

function escapeTableCell(text: string): string {
    // Escape pipes (table delimiter), normalize newlines, and HTML-escape angle brackets
    // so summaries like "PropertyMap<T>.Find" don't get parsed as Vue components.
    return text
        .replace(/\|/g, "\\|")
        .replace(/\n/g, " ")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;");
}

/* ── Doc-node → Markdown ────────────────────────────────────────── */

interface RenderCtx {
    pkg: PackageDoc;
    basePath: string;
    inline: boolean;
}

function renderDocNodes(nodes: DocNode[] | undefined, ctx: RenderCtx): string {
    if (!nodes || !nodes.length) return "";
    return nodes.map((n) => renderDocNode(n, ctx)).join("");
}

function renderDocNode(node: DocNode, ctx: RenderCtx): string {
    switch (node.kind) {
        case "text":
            return ctx.inline ? escapeMd(node.text ?? "") : escapeMd(node.text ?? "");

        case "code":
            return "`" + (node.text ?? "") + "`";

        case "codeblock": {
            const lang = node.language || "csharp";
            return `\n\n\`\`\`${lang}\n${(node.text ?? "").replace(/\n+$/, "")}\n\`\`\`\n\n`;
        }

        case "cref": {
            const ref = node.value ?? "";
            const link = resolveCref(ref, ctx);
            const label = link ? link.label : cleanCrefLabel(ref);
            return link
                ? `[\`${label}\`](${link.href})`
                : "`" + label + "`";
        }

        case "href": {
            const url = node.value ?? "";
            const text = node.text ?? url;
            return `[${escapeMd(text)}](${url})`;
        }

        case "langword":
            return "`" + (node.value ?? "") + "`";

        case "paramref":
        case "typeparamref":
            return "*`" + (node.value ?? "") + "`*";

        case "para": {
            const inner = renderDocNodes(node.children, { ...ctx, inline: true });
            return ctx.inline ? inner : `\n\n${inner}\n\n`;
        }

        case "note": {
            const noteType = node.value ?? "note";
            const inner = renderDocNodes(node.children, { ...ctx, inline: false });
            const containerType = noteType === "warning" || noteType === "caution"
                ? "warning"
                : noteType === "important" ? "important"
                : noteType === "tip" ? "tip"
                : "note";
            return `\n\n::: ${containerType}\n${inner.trim()}\n:::\n\n`;
        }

        case "list": {
            if (node.style === "table") {
                const headTerm = node.header?.term ? renderDocNodes(node.header.term, { ...ctx, inline: true }) : "";
                const headDesc = node.header?.description ? renderDocNodes(node.header.description, { ...ctx, inline: true }) : "";
                const rows = (node.items ?? []).map((it) => {
                    const term = it.term ? renderDocNodes(it.term, { ...ctx, inline: true }) : "";
                    const desc = renderDocNodes(it.description, { ...ctx, inline: true });
                    return `| ${escapeTableCell(term)} | ${escapeTableCell(desc)} |`;
                });
                return `\n\n| ${escapeTableCell(headTerm) || "Term"} | ${escapeTableCell(headDesc) || "Description"} |\n|---|---|\n${rows.join("\n")}\n\n`;
            }
            const marker = node.style === "number" ? (i: number) => `${i + 1}.` : () => "-";
            const items = (node.items ?? []).map((it, i) => {
                const term = it.term ? `**${renderDocNodes(it.term, { ...ctx, inline: true })}** — ` : "";
                const desc = renderDocNodes(it.description, { ...ctx, inline: true });
                return `${marker(i)} ${term}${desc}`;
            });
            return `\n\n${items.join("\n")}\n\n`;
        }

        default:
            return "";
    }
}

interface CrefLink {
    href: string;
    label: string;
}

/** Resolve a `cref` value (e.g. "T:Ns.Foo", "M:Ns.Foo.Bar(System.Int32)") to a link. */
function resolveCref(cref: string, ctx: RenderCtx): CrefLink | null {
    const parsed = parseCref(cref);
    if (!parsed) return null;
    const { prefix, fullName } = parsed;

    if (prefix === "T" || prefix === "") {
        const target = findTypeByFullName(ctx.pkg.types, fullName);
        if (target) {
            return {
                href: `${ctx.basePath}/${typeSlug(target)}/`,
                label: target.name,
            };
        }
        return null;
    }

    if (["M", "P", "F", "E"].includes(prefix)) {
        const parenIdx = fullName.indexOf("(");
        const withoutParams = parenIdx >= 0 ? fullName.slice(0, parenIdx) : fullName;
        const withoutArity = withoutParams.replace(/``\d+/g, "");
        const lastDot = withoutArity.lastIndexOf(".");
        if (lastDot > 0) {
            const ownerFull = withoutArity.slice(0, lastDot);
            const memberName = withoutArity.slice(lastDot + 1);
            const target = findTypeByFullName(ctx.pkg.types, ownerFull);
            if (target) {
                const anchor = memberName.toLowerCase().replace(/[^a-z0-9]+/g, "-");
                return {
                    href: `${ctx.basePath}/${typeSlug(target)}/#${anchor}`,
                    label: `${target.name}.${memberName}`,
                };
            }
        }
    }
    return null;
}

function parseCref(cref: string): { prefix: string; fullName: string; simpleName: string } | null {
    if (!cref) return null;
    let prefix = "";
    let body = cref;
    if (cref.length > 1 && cref[1] === ":") {
        prefix = cref[0];
        body = cref.slice(2);
    }
    const simple = shortTypeName(body);
    return { prefix, fullName: body, simpleName: simple };
}

function cleanCrefLabel(cref: string): string {
    const parsed = parseCref(cref);
    if (!parsed) return cref;
    return shortTypeName(parsed.fullName);
}

/** Plain-text flatten for first-line summaries / descriptions in tables. */
function flattenDocText(nodes: DocNode[] | undefined): string {
    if (!nodes) return "";
    const parts: string[] = [];
    for (const n of nodes) {
        switch (n.kind) {
            case "text": parts.push(n.text ?? ""); break;
            case "code": parts.push(n.text ?? ""); break;
            case "codeblock": parts.push(""); break;
            case "cref": parts.push(cleanCrefLabel(n.value ?? "")); break;
            case "href": parts.push(n.text ?? n.value ?? ""); break;
            case "langword": parts.push(n.value ?? ""); break;
            case "paramref":
            case "typeparamref": parts.push(n.value ?? ""); break;
            case "para": parts.push(flattenDocText(n.children)); break;
            case "note": parts.push(flattenDocText(n.children)); break;
            default: break;
        }
    }
    return parts.join("").replace(/\s+/g, " ").trim();
}

/* ── Page rendering ─────────────────────────────────────────────── */

function modifierBadges(t: Type): string[] {
    const out: string[] = [];
    if (t.isAbstract) out.push("abstract");
    if (t.isStatic) out.push("static");
    if (t.isSealed && !["enum", "delegate", "struct", "record struct"].includes(t.kind)) out.push("sealed");
    if (t.isReadOnly) out.push("readonly");
    return out;
}

function renderPackageMd(pkg: PackageDoc, basePath: string): string {
    const validTypes = pkg.types.filter((t) => t.name);
    const ctx: RenderCtx = { pkg, basePath, inline: true };

    // Group by namespace
    const byNs = new Map<string, Type[]>();
    for (const t of validTypes) {
        const ns = t.namespace ?? "";
        const arr = byNs.get(ns) ?? [];
        arr.push(t);
        byNs.set(ns, arr);
    }
    const namespaces = [...byNs.entries()].sort(([a], [b]) => a.localeCompare(b));

    const lines: string[] = [];
    lines.push(`# ${pkg.package.name}`);
    lines.push("");
    lines.push(`> API reference for **${pkg.package.name}** \`v${pkg.package.version}\` · target framework \`${pkg.package.targetFramework ?? "—"}\``);
    lines.push("");
    lines.push(`**${validTypes.length}** types across **${namespaces.length}** namespaces.`);
    lines.push("");

    for (const [ns, types] of namespaces) {
        lines.push(`## ${ns || "(global)"}`);
        lines.push("");
        lines.push("| Type | Kind | Description |");
        lines.push("|---|---|---|");
        const sortedTypes = [...types].sort((a, b) => a.name.localeCompare(b.name));
        for (const t of sortedTypes) {
            const href = `${basePath}/${typeSlug(t)}/`;
            const nameMd = `[\`${typeDisplayName(t)}\`](${href})`;
            const kind = t.kind;
            const summary = escapeTableCell(flattenDocText(t.docs?.summary));
            lines.push(`| ${nameMd} | ${kind} | ${summary} |`);
        }
        lines.push("");
    }
    return lines.join("\n");
}

function renderTypeMd(t: Type, pkg: PackageDoc, basePath: string): string {
    const ctx: RenderCtx = { pkg, basePath, inline: false };
    const lines: string[] = [];

    const display = typeDisplayName(t);
    lines.push(`# \`${display}\``);
    lines.push("");
    lines.push(`[← ${pkg.package.name}](${basePath}/)`);
    lines.push("");

    /* Metadata box */
    const meta: string[] = [];
    meta.push(`**Namespace** \`${t.namespace ?? ""}\``);
    meta.push(`**Kind** ${KIND_LABEL[t.kind] ?? t.kind}`);
    meta.push(`**Assembly** \`${pkg.package.name}.dll\``);
    const mods = modifierBadges(t);
    if (mods.length) meta.push(`**Modifiers** ${mods.map((m) => `\`${m}\``).join(" ")}`);
    if (t.baseType && t.baseType !== "System.Object") {
        const baseTarget = findTypeByFullName(pkg.types, t.baseType);
        const label = shortTypeName(t.baseType);
        meta.push(`**Inherits** ${baseTarget ? `[\`${label}\`](${basePath}/${typeSlug(baseTarget)}/)` : "`" + label + "`"}`);
    }
    if (t.interfaces && t.interfaces.length) {
        const ifaces = t.interfaces.map((iface) => {
            const target = findTypeByFullName(pkg.types, iface);
            const label = shortTypeName(iface);
            return target ? `[\`${label}\`](${basePath}/${typeSlug(target)}/)` : "`" + label + "`";
        });
        meta.push(`**Implements** ${ifaces.join(", ")}`);
    }
    lines.push(meta.join(" · "));
    lines.push("");

    /* Summary */
    if (t.docs?.summary?.length) {
        lines.push(renderDocNodes(t.docs.summary, ctx).trim());
        lines.push("");
    }

    /* Remarks */
    if (t.docs?.remarks?.length) {
        lines.push("## Remarks");
        lines.push("");
        lines.push(renderDocNodes(t.docs.remarks, ctx).trim());
        lines.push("");
    }

    /* Enum members */
    if (t.kind === "enum" && t.enumMembers?.length) {
        lines.push("## Members");
        lines.push("");
        lines.push("| Name | Value | Description |");
        lines.push("|---|---|---|");
        for (const em of t.enumMembers) {
            lines.push(`| \`${em.name}\` | ${em.value} | ${escapeTableCell(em.description ?? "")} |`);
        }
        lines.push("");
    }

    /* Members grouped by kind */
    if (t.members?.length) {
        const groups = new Map<string, Member[]>();
        for (const m of t.members) {
            const arr = groups.get(m.kind) ?? [];
            arr.push(m);
            groups.set(m.kind, arr);
        }
        for (const kind of MEMBER_KIND_ORDER) {
            const list = groups.get(kind);
            if (!list?.length) continue;
            lines.push(`## ${MEMBER_KIND_LABEL[kind]}`);
            lines.push("");
            const sorted = [...list].sort((a, b) => a.name.localeCompare(b.name));
            for (const m of sorted) {
                lines.push(...renderMemberMd(m, t, ctx));
            }
        }
    }
    return lines.join("\n");
}

function renderMemberMd(m: Member, owner: Type, ctx: RenderCtx): string[] {
    const lines: string[] = [];
    // Use a stable, descriptive heading; VuePress auto-generates anchors from heading text.
    const displayName = m.name === ".ctor"
        ? `${owner.name} constructor`
        : m.name;
    lines.push(`### \`${displayName}\``);
    lines.push("");

    if (m.signature) {
        lines.push("```csharp");
        lines.push(m.signature);
        lines.push("```");
        lines.push("");
    }

    /* Modifiers row */
    const modBits: string[] = [];
    if (m.isStatic) modBits.push("`static`");
    if (m.isAbstract) modBits.push("`abstract`");
    if (m.isVirtual) modBits.push("`virtual`");
    if (m.isOverride) modBits.push("`override`");
    if (m.isSealed) modBits.push("`sealed`");
    if (m.isReadOnly) modBits.push("`readonly`");
    if (m.isAsync) modBits.push("`async`");
    if (m.isExtension) modBits.push("`extension`");
    if (modBits.length) {
        lines.push(modBits.join(" "));
        lines.push("");
    }

    /* Summary */
    if (m.docs?.summary?.length) {
        lines.push(renderDocNodes(m.docs.summary, ctx).trim());
        lines.push("");
    }

    /* Parameters */
    if (m.parameters?.length) {
        lines.push("**Parameters**");
        lines.push("");
        lines.push("| Name | Type | Description |");
        lines.push("|---|---|---|");
        for (const p of m.parameters) {
            const desc = m.docs?.parameters?.[p.name]
                ? escapeTableCell(flattenDocText(m.docs.parameters[p.name]))
                : "";
            const typeText = `\`${shortTypeName(p.type)}\``;
            const nameText = `\`${p.name}\``;
            lines.push(`| ${nameText} | ${typeText} | ${desc} |`);
        }
        lines.push("");
    }

    /* Returns */
    if (m.returnType && m.returnType !== "void") {
        const returnsText = m.docs?.returns?.length
            ? renderDocNodes(m.docs.returns, { ...ctx, inline: true }).trim()
            : "";
        lines.push(`**Returns** \`${shortTypeName(m.returnType)}\`${returnsText ? ` — ${returnsText}` : ""}`);
        lines.push("");
    } else if (m.type) {
        const valueText = m.docs?.value?.length
            ? renderDocNodes(m.docs.value, { ...ctx, inline: true }).trim()
            : "";
        lines.push(`**Type** \`${shortTypeName(m.type)}\`${valueText ? ` — ${valueText}` : ""}`);
        lines.push("");
    }

    /* Remarks */
    if (m.docs?.remarks?.length) {
        lines.push("**Remarks**");
        lines.push("");
        lines.push(renderDocNodes(m.docs.remarks, ctx).trim());
        lines.push("");
    }

    return lines;
}

/* ── Plugin entry point ─────────────────────────────────────────── */

export function csharpApiReferencePlugin(options: CsharpApiReferenceOptions): Plugin {
    const dataDir = options.dataDir;
    const basePath = options.basePath.replace(/\/+$/, "");

    return {
        name: "csharp-api-reference",
        async onInitialized(app: App) {
            let entries: string[] = [];
            try {
                entries = (await fs.readdir(dataDir)).filter((f) => f.endsWith(".json"));
            } catch {
                return; // No data dir yet — skip silently.
            }
            if (entries.length === 0) return;

            for (const file of entries) {
                const full = path.join(dataDir, file);
                let pkg: PackageDoc;
                try {
                    pkg = JSON.parse(await fs.readFile(full, "utf8"));
                } catch (err) {
                    console.warn(`[csharp-api-reference] failed to parse ${file}:`, err);
                    continue;
                }

                /* Package landing page */
                const landing = await createPage(app, {
                    path: `${basePath}/`,
                    frontmatter: {
                        title: `${pkg.package.name} API`,
                        description: `API reference for ${pkg.package.name} v${pkg.package.version}`,
                    },
                    content: renderPackageMd(pkg, basePath),
                });
                app.pages.push(landing);

                /* One page per type */
                const seen = new Set<string>();
                for (const t of pkg.types) {
                    if (!t.name) continue;
                    const slug = typeSlug(t);
                    if (seen.has(slug)) continue; // first-wins on duplicates
                    seen.add(slug);
                    const page: Page = await createPage(app, {
                        path: `${basePath}/${slug}/`,
                        frontmatter: {
                            title: typeDisplayName(t),
                            description: flattenDocText(t.docs?.summary).slice(0, 160) || `${t.kind} ${t.name}`,
                        },
                        content: renderTypeMd(t, pkg, basePath),
                    });
                    app.pages.push(page);
                }
            }
        },
    };
}

export default csharpApiReferencePlugin;
