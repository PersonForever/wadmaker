# WadMaker
*"I certainly hope you know wad you're doing!"*

## Table of contents
- [Overview](#overview)
    - [Requirements](#requirements)
- [How to use](#how-to-use)
    - [Basic usage](#basic-usage)
    - [Advanced options](#advanced-options)
    - [Texture-specific settings](#texture-specific-settings)
- [About Half-Life textures](#about-half-life-textures)
- [Comparisons](#comparisons)
- [Libraries](#libraries)

## Overview
WadMaker is a command-line tool that can turn directories that contain images into Half-Life wad files. Existing wad files can be updated more quickly because only added, modified and removed images are processed by default. WadMaker can also extract textures from wad and bsp files, or remove embedded textures from bsp files.

WadMaker directly supports png, jpg, gif, bmp and tga files, and can be configured to call external conversion tools for other formats. It will automatically create a suitable 256-color palette for each image. It will also apply a limited form of dithering, which can be disabled if necessary. For transparent textures, the alpha channel of the input image is compared against a configurable threshold, but it is also possible to treat a specific input color as transparent. For water textures, the fog color and intensity are derived from the image itself, but they can also be specified explicitly. All these texture-specific settings can be overridden with a plain-text wadmaker.config file in the images directory.

### Requirements
WadMaker requires .NET Framework 4.7.2 or higher. If you're using an up-to-date Windows 10 then you're ready to go. If you're using Windows 7 or 8 then you may need to [download .NET Framework from Microsoft's website](https://dotnet.microsoft.com/download/dotnet-framework).

## How to use
### Basic usage
For basic usage, directories and files can be dragged onto `WadMaker.exe`:
- To **make a wad file**, drag the folder that contains your images onto `WadMaker.exe`. A wad file with the same name as the directory will be created next to the directory. If the wad file already exists, then it will be updated, with only added, modified and removed images being processed.
- To **extract textures** from a wad or bsp file, drag the file onto `WadMaker.exe`. All textures will be saved to a 'filename_extracted' directory that will be created next to the wad or bsp file. This also works for bsp files with embedded textures. Existing images in this directory will not be overwritten by default.

### Advanced options
The behavior of WadMaker can be modified with several command-line options. To use these, you will have to call WadMaker from a command-line or from a batch file. The following options are available (options must be put before the input directory or file path):
- **full** - Forces WadMaker to do a full wad rebuild, instead of updating an existing wad file.
- **subdirs** - Makes WadMaker look for images in sub-directories.
- **mipmaps** - Enables the extraction of texture mipmaps.
- **overwrite** - Enables overwriting of existing files when extracting textures.
- **remove** - Removes embedded textures from a bsp file.

It is also possible to specify a custom output location when making a wad file. For example:
`"C:\HL\tools\WadMaker.exe" -subdirs "C:\HL\mymod\textures\chapter1" "C:\Steam\steamapps\common\Half-Life\mymod\chapter1.wad"`
will take all images in `C:\HL\mymod\textures\chapter1` and its sub-directories, and use them to create or update `C:\Steam\steamapps\common\Half-Life\mymod\chapter1.wad`.

Likewise, it's possible to save extracted textures to a specific location. For example:
`"C:\HL\tools\WadMaker.exe" -mipmaps -overwrite "C:\Steam\steamapps\common\Half-Life\valve\halflife.wad" "C:\HL\extracted\halflife"`
will extract all textures, including mipmaps, from `C:\Steam\steamapps\common\Half-Life\valve\halflife.wad`, and save them to `C:\HL\extracted\halflife`, overwriting any existing files in that directory.

### Texture-specific settings
WadMaker lets you override various settings per texture, or per group of textures, by creating a `wadmaker.config` file in your images directory. This is a plain-text file, where each line can hold a settings rule.

A settings line starts with a texture name or a name pattern, followed by one or more settings. Empty lines and comments are ignored. For example:

    // This is a comment. The next 3 lines contain texture settings:
    bluewater    water-fog: 0 0 255 127
    {lab*        dithering: none     transparency-threshold: 200
    *.psd        converter: '"C:\Tools\PsdToPngConverter.exe"'       arguments: '/in="{input}" /out="{output}"'
This explicitly defines the water fog color and intensity for a texture named 'bluewater'. It also disables dithering and sets a custom transparency threshold for all textures whose name starts with '{lab'. Finally, it tells WadMaker to call a converter application for each .psd file in the image directory - WadMaker will then use the output image produced by that application.

WadMaker keeps track of settings history in a wadmaker.dat file. This enables it to only update textures whose settings have been modified (if `-full` mode is not enabled).

The following settings can be configured per texture:
- **dithering** - Either `none` or `floyd-steinberg`. By default, Floyd-Steinberg dithering is applied.
- **dither-scale** - A value between 0 (disables dithering) and 1 (full error diffusion). The default is 0.75, which softens the effect somewhat.
- **transparency-threshold** - A value between 0 and 255. The default is 128. Any pixel whose alpha value is below this threshold will be marked as transparent. This only applies to textures whose name starts with a `{`.
- **transparency-color** - A color, written as 3 whitespace-separated numbers (`red green blue`), with each number between 0 and 255. Pixels with this color will be marked as transparent. This only applies to textures whose name starts with a `{`. By default, no color is specified.
- **water-fog** - The water fog color and intensity, written as 4 whitespace-separated numbers (`red green blue intensity`), with each number between 0 and 255. By default, the fog color and intensity are derived from the average color of the image. This only applies to textures whose name starts with a `!`.
- **converter** - The path of an application that can convert a file into a png file. If the path contains spaces then it should be surrounded by double quotes. The whole path, including any double quotes, must be delimited by single quotes. Any single quotes in the path itself must be escaped with a `\`. For example, the path `C:\what's that.exe` should be written as `'"C:\what\'s that.exe"'`.
- **arguments** - The arguments that will be passed to the converter application, surrounded by single quotes. This must contain the placeholders `{input}` and `{output}`. WadMaker will replace `{input}` with the path of the current file, and `{output}` with a path where the converter tool must save its output. This output file is then used to create a texture. As with the `converter` setting, the whole arguments list must be delimited by single quotes, and any path that contains spaces (including `{input}` and `{output}`) should be surrounded by double quotes.

### About Half-Life textures
Half-Life textures use a 256-color palette, and their width and height must be multiples of 16. Texture names cannot be longer than 15 characters and cannot contain spaces.

The game supports several special texture types. The type of a texture depends on the first part of its name:
- `{` is for textures with transparent areas. The last color in the palette (index 255) is used for transparent pixels.
- `!` is for water textures. The 4th palette color (index 3) is used as water fog color, and the red channel of the 5th color (index 4) is used as fog intensity (a higher intensity results in a lower view distance).
- `scroll` (lowercase) is for scrolling textures. These are used in conjunction with the `func_conveyor` entity.
- `+0` - `+9` (and `+A` to `+J`) are for animated textures. The game will automatically cycle to the next texture in the sequence every 0.1 second. If the texture is applied to an entity that can be toggled, then the game will switch between the numbered sequence and the 'lettered' sequence whenever the entity is toggled.

Additionally, some textures serve a special purpose for the map compile tools, such as:
- `SKY` is used to mark brushes as a 'sky brushes'. Such brushes won't be visible in-game, but the skybox will be shown instead.
- `ORIGIN` is used to create 'origin brushes', whose center is used as the origin of brush-based entities.
- `CLIP` is used to block player movement.
- `NULL` is used to remove surfaces that are not visible to the player.
- `HINT` (along with `SKIP`) is used to force a bsp node cut. Strategic use of this can improve performance.

## Comparisons
Just like Wally, WadMaker can convert true-color images to the 256-color indexed format that Half-Life uses. For textures that do not contain a wide range of colors and gradients, this often does not lead to a perceptible loss of quality. In cases where this does matter, WadMaker tends to produce better results than Wally due to its use of dithering, but it does worse than IrfanView:

![input, WadMaker, IrfanView, Wally](/documentation/images/comparison.png "input, WadMaker, IrfanView, Wally")
*(free texture downloaded from https://textures.pixel-furnace.com, scaled-down to 256x256 pixels)*

This is one reason why WadMaker provides a converter setting: to make it possible to let IrfanView or another application handle the color reduction, while WadMaker takes care of the actual wad file making. Simply add the following line to your `wadmaker.config` file:

    if_*    converter: '"C:\Program Files\IrfanView\i_view64.exe"' arguments: '"{input}" /silent /bpp=8 /convert="{output}"'
Or, when using advanced batch settings, save those settings to a `i_view64.ini` file, and specify the directory in which that ini file is located:

    if_*    converter: '"C:\Program Files\IrfanView\i_view64.exe"' arguments: '"{input}" /silent /ini="C:\custom_irfanview_settings_dir" /advancedbatch /convert="{output}"'
The `if_*` name pattern here means that any image file whose name starts with `if_` will be processed by IrfanView, so switching between standard WadMaker and IrfanView behavior can be done by simply renaming an image.

## Libraries
WadMaker uses the ImageSharp library, which is licensed under the Apache License 2.0.