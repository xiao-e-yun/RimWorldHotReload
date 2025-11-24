# RimWorld Hot Reload
A simple mod for hot reloading in RimWorld.

Supports Types:
- Textures reloading
- Audio reloading
- Def reloading

Supports Call Methods:
- Manually trigger (Debug Actions &gt; Mods &gt; Hot Reload)
- File System Watcher (auto reload on file change)
- HTTP API (POST /hot-reload)

## Usage:
1. Install the mod.
2. Open the your mod folder.
3. Create a "hotReload.json" file with the following content:
```json
{
    "enabled": true,
    "assets": true,
    "defs": true,
    "watch": true,
    "api": true
}
```
4. Modify your mod files as needed.
- `enabled`: Enable or disable hot reload.
- `assets`: Enable or disable assets reloading.
- `defs`: Enable or disable defs reloading.
- `watch`: Enable or disable file system watcher.
- `api`: Enable or disable HTTP API.

5. Start RimWorld, you can open console to check more info.

## For Developers:
[Github Repository](https://github.com/xiao-e-yun/RimWorldHotReload). 
Requries [Bun](https://bun.sh/), [Git](https://git-scm.com/).

You can use the script to build the mod:
```bash
bun run build.ts
```

If you don't have Bun, you can manually build:
```bash
dotnet build /p:DebugType=None /p:DebugSymbols=false
cp ./about/ ./dist/About/ -r 
```
Then you need modify `dist/About/About.xml` to set your mod info (like `DESCRIPTION`, `MOD_VERSION`).
