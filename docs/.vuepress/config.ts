import { dl } from "@mdit/plugin-dl";
import viteBundler from "@vuepress/bundler-vite";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {defineUserConfig} from "vuepress";
import {fs} from "vuepress/utils";
import {hopeTheme} from "vuepress-theme-hope";
import {resolveMultiSamplesPath} from "./lib/samples";
import {linkCheckPlugin} from "./markdown/linkCheck";
import {replaceLinkPlugin} from "./markdown/replaceLink";
import {importCodePlugin} from "./markdown/xode/importCodePlugin";
import {csharpApiReferencePlugin} from "./plugins/csharpApiReference";

const __dirname = fileURLToPath(new URL(".", import.meta.url));

const projectionSamplesPath = "https://raw.githubusercontent.com/kurrent-io/KurrentDB/53f84e55ea56ccfb981aff0e432581d72c23fbf6/samples/http-api/data/";

export default defineUserConfig({
    base: "/",
    dest: "public",
    title: "KurrentDB Docs",
    description: "Event-native database",
    bundler: viteBundler(),
    plugins: [
        csharpApiReferencePlugin({
            dataDir: resolve(__dirname, "data/pkgs"),
            basePath: "/client/api",
        }),
    ],
    markdown: {importCode: false},
    extendsMarkdown: md => {
        md.use(importCodePlugin, {
            handleImportPath: s => resolveMultiSamplesPath(s)
        });
        md.use(linkCheckPlugin);
        md.use(replaceLinkPlugin, {
            replaceLink: (link: string, _) => link
                .replace("@httpapi/data/", projectionSamplesPath)
        });
        md.use(dl);
    },
    theme: hopeTheme({
        logo: "/Kurrent Logo - Plum.svg",
        logoDark: "/Kurrent Logo - White.svg",
        docsDir: ".",
        toc: true,
        sidebar: {
            "/client/": [
                {
                    text: "Get started",
                    collapsible: false,
                    children: [
                        "quickstart",
                        {
                            text: "Connect",
                            prefix: "connect/",
                            collapsible: true,
                            children: "structure",
                        },
                    ],
                },
                {
                    text: "Modules",
                    collapsible: false,
                    children: [
                        {
                            text: "Streams",
                            prefix: "streams/",
                            collapsible: true,
                            children: "structure",
                        },
                        "indexes",
                        {
                            text: "Schema Registry",
                            prefix: "schema-registry/",
                            link: "schema-registry/",
                            collapsible: true,
                            children: "structure",
                        },
                        "connectors",
                        "admin",
                    ],
                },
                {
                    text: "Concepts",
                    collapsible: false,
                    children: [
                        "serialization",
                        "error-handling",
                    ],
                },
                {
                    text: "Migrate your project",
                    prefix: "migrate/",
                    link: "migrate/",
                    collapsible: true,
                    children: "structure",
                },
                {
                    text: "API Reference",
                    link: "/client/api/",
                    icon: "code",
                    collapsible: false,
                },
            ],
            "/processor/": "structure",
        },
        navbar: [
            {
                text: "Client",
                link: "/client/quickstart",
            },
            {
                text: "Processor",
                link: "/processor/getting-started",
            },
        ],
        markdown: {
            figure: true,
            imgLazyload: true,
            imgMark: true,
            imgSize: true,
            tabs: true,
            codeTabs: true,
            component: true,
            mermaid: true,
            highlighter: {
                type: "shiki",
                themes: {
                    light: "one-light",
                    dark: "one-dark-pro",
                }
            }
        },
        plugins: {
            search: {},
            sitemap:{
                devServer: process.env.NODE_ENV === 'development',
                modifyTimeGetter: (page, app) => {
                    // Virtual pages (e.g. plugin-generated API reference) have no file on disk.
                    if (!page.filePathRelative) return new Date().toISOString();
                    return fs.statSync(app.dir.source(page.filePathRelative)).mtime.toISOString();
                }
            },
            components: {
                components: ["Badge", "VPBanner", "VPCard", "VidStack"]
            },
        }
    }),
});
