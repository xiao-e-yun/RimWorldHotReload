#!/usr/bin/env bun
import { promises as fs } from "fs";
import path from "path";
import { $ } from "bun";

// simplified: we will only read `about/About.xml` and write a single `dist/About.xml` (no subfolder)

async function main() {
    await $`dotnet build /p:DebugType=None /p:DebugSymbols=false`;

    const root = process.cwd();
    const aboutSrc = path.join(root, "about");
    const outDir = path.join(root, "dist", "About");
    await fs.mkdir(outDir, { recursive: true });
    await fs.cp(aboutSrc, outDir, { recursive: true });

    const readmePath = path.join(root, "README.md");
    const readme = await fs.readFile(readmePath, "utf8").catch(() => "");

    let tag = "0.0.0";
    try {
        const proc = await $`git describe --tags --abbrev=0`;
        tag = new TextDecoder().decode(proc.stdout).trim() || tag;
    } catch (e) {
        // ignore, leave default
    }

    const aboutXmlPath = path.join(outDir, "About.xml");
    const aboutXml = await fs.readFile(aboutXmlPath, "utf8").catch(() => {
        console.error("about/About.xml not found");
        return "";
    });

    const replaced = aboutXml.replace("MOD_VERSION", tag).replace("DESCRIPTION", readme);
    await fs.writeFile(aboutXmlPath, replaced, "utf8");

    console.log("Used version:", tag);
}

main().catch((e) => {
    console.error(e);
    process.exit(1);
});
