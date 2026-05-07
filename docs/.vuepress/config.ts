import { dl } from "@mdit/plugin-dl";
import viteBundler from "@vuepress/bundler-vite";
import {defineUserConfig} from "vuepress";
import {fs} from "vuepress/utils";
import {hopeTheme} from "vuepress-theme-hope";
import {resolveMultiSamplesPath} from "./lib/samples";
import {linkCheckPlugin} from "./markdown/linkCheck";
import {replaceLinkPlugin} from "./markdown/replaceLink";
import {importCodePlugin} from "./markdown/xode/importCodePlugin";

const projectionSamplesPath = "https://raw.githubusercontent.com/kurrent-io/KurrentDB/53f84e55ea56ccfb981aff0e432581d72c23fbf6/samples/http-api/data/";

export default defineUserConfig({
    base: "/",
    dest: "public",
    title: "KurrentDB Docs",
    description: "Event-native database",
    bundler: viteBundler(),
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
            "/client/": "structure",
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
                modifyTimeGetter: (page, app) =>
                    fs.statSync(app.dir.source(page.filePathRelative!)).mtime.toISOString()
            },
            components: {
                components: ["Badge", "VPBanner", "VPCard", "VidStack"]
            },
        }
    }),
});
