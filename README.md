# Olympus.FNA - Cross-platform Celeste Modded Launcher & Manager

### License: MIT

...because Everest.Installer was bad and building Olympus with Love2D is a mess.

----

<a href="https://discord.gg/6qjaePQ"><img align="right" alt="Mt. Celeste Climbing Association" src="https://discordapp.com/api/guilds/403698615446536203/embed.png?style=banner2" /></a>

[**Check out the everest website (not documented yet)**](https://everestapi.github.io/)

**Work in progress!**

## Dependencies
- [FNA](https://fna-xna.github.io/)
- [FontStashSharp](https://github.com/rds1983/FontStashSharp)

## Building
### Requirements
- Make sure to clone all submodules
- Have .net core 7 or later
### All platforms
- Run `dotnet build` and everything should set up automatically
#### On windows
- Make sure that it built targeting `net7.0-windows` instead of `net7.0`. You can verify this by looking at where did the built files get put (should be somehting like `bin/Debug/net7.0-windows/...`)

## Project structure
This project is organized into 4 principal directories:
- FNA: This is the engine itself, the intended way of developing with FNA is to have it as a submodule, not as a package.
- FontStashSharp: Font utils and renderer. The same principle from FNA applies here.
- Olympus.FNA: This is where the actual project lies, most code goes here.
- Olympus.FNA.Gen: Code generation for certain Olympus.FNA aspects.

## Origins
This project was initially created by 0x0ade, with them having done most of the UI and style work. Then, after the project was abandoned for multiple years, I (Wartori) picked it up again, and implemented most of the functionality for the app in order to get it to a feature-complete product. It is currently mantained by Wartori.
