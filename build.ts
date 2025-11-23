#!/usr/bin/env bun
import { promises as fs } from "fs";
import path from "path";
import { $ } from "bun";

// simplified: we will only read `about/About.xml` and write a single `dist/About.xml` (no subfolder)

async function main() {
    const buildCSharp = $`dotnet build /p:DebugType=None /p:DebugSymbols=false`;

  const root = process.cwd();
  const readmePath = path.join(root, "README.md");
  const aboutSrc = path.join(root, "about", "About.xml");
  const outDir = path.join(root, "dist", "About");
  await fs.mkdir(outDir, { recursive: true });

  const readme = await fs.readFile(readmePath, "utf8").catch(() => "");

  let tag = "0.0.0";
  try {
    const proc = await $`git describe --tags --abbrev=0`;
    tag = new TextDecoder().decode(proc.stdout).trim() || tag;
  } catch (e) {
    // ignore, leave default
  }

  const aboutXml = await fs.readFile(aboutSrc, "utf8").catch(() => {
    console.error("about/About.xml not found");
    return "";
  });

  const replaced = aboutXml.replace(/DESCRIPTION/g, readme).replace(/MOD_VERSION/g, tag);

  const outPath = path.join(outDir, "About.xml");
  await fs.writeFile(outPath, replaced, "utf8");

  await buildCSharp;
  console.log("Built:", outPath);
  console.log("Used version:", tag);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
