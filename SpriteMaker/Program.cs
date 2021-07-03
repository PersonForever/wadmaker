﻿using Shared;
using Shared.FileFormats;
using Shared.Sprites;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace SpriteMaker
{
    class ProgramSettings
    {
        // General settings:
        public bool IncludeSubDirectories { get; set; }         // -subdirs         also processes files in sub-directories (applies to both sprite building and extracting)

        // Build settings:
        public bool FullRebuild { get; set; }                   // -full            forces a full rebuild instead of an incremental one
        public bool EnableSubDirectoryRemoval { get; set; }     // -subdirremoval   enables deleting of output sub-directories when input sub-directories are removed

        // Extract settings:
        public bool Extract { get; set; }                       // -extract         extracts all sprites in the input directory (this is also enabled if the input file is a .spr file)
        public bool ExtractAsSpriteSheet { get; set; }          // -spritesheet     extracts multi-frame sprites as spritesheets instead of image sequences
        public bool OverwriteExistingFiles { get; set; }        // -overwrite       extract mode only, enables overwriting of existing image files (off by default)
        public bool ExtractAsGif { get; set; }                  // -gif             extract sprites as (animated) gif files

        // Other settings:
        public string InputPath { get; set; }                   // An image or sprite file, or a directory full of images (or sprites, if -extract is set)
        public string OutputPath { get; set; }                  // Output sprite or image path, or output directory path

        public bool DisableFileLogging { get; set; }            // -nologfile       disables logging to a file (parent-directory\spritemaker.log)
    }

    /*
    Usage:

    SpriteMaker.exe input (output)
        If output is not specified, then it will be set to a value that depends on the input and the kind of input.
        If input is:
        - a sprite file: SpriteMaker will 'extract' the sprite file into a png file (or files, for multi-frame pngs, depending on command-line option -nospritesheet)
        - an image file: SpriteMaker will turn it into a sprite (if image name contains a .N part, then all related frame images are also used to create a multi-frame sprite)
        - a directory: SpriteMaker will turn all images in the folder into sprites (unless the -extract command-line option is specified, then it'll create images from all sprite files)
    */
    class Program
    {
        static TextWriter LogFile;


        static void Main(string[] args)
        {
            try
            {
                Log($"{Assembly.GetExecutingAssembly().GetName().Name}.exe {string.Join(" ", args)}");

                var settings = ParseArguments(args);
                if (settings.Extract)
                {
                    if (!string.IsNullOrEmpty(Path.GetExtension(settings.InputPath)))
                        ExtractSprite(settings.InputPath, settings.OutputPath, settings.ExtractAsSpriteSheet, settings.OverwriteExistingFiles);
                    else
                        ExtractSprites(settings.InputPath, settings.OutputPath, settings.ExtractAsSpriteSheet, settings.OverwriteExistingFiles, settings.IncludeSubDirectories);
                }
                else
                {
                    if (!settings.DisableFileLogging)
                    {
                        // For directories, use specific log files, but for individual image-to-sprite conversion, reuse a single log file to reduce clutter:
                        var isInputDirectory = Directory.Exists(settings.InputPath);
                        var logFilePath = Path.Combine(
                            Path.GetDirectoryName(settings.InputPath),
                            isInputDirectory ? $"spritemaker - {Path.GetFileName(settings.InputPath)}.log" : "spritemaker.log");
                        LogFile = new StreamWriter(logFilePath, false, Encoding.UTF8);
                        LogFile.WriteLine($"{Assembly.GetExecutingAssembly().GetName().Name}.exe {string.Join(" ", args)}");
                    }

                    if (!string.IsNullOrEmpty(Path.GetExtension(settings.InputPath)))
                        MakeSprite(settings.InputPath, settings.OutputPath);
                    else
                        MakeSprites(settings.InputPath, settings.OutputPath, settings.FullRebuild, settings.IncludeSubDirectories, settings.EnableSubDirectoryRemoval);
                }
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.GetType().Name}: '{ex.Message}'.");
                Log(ex.StackTrace);
            }
            finally
            {
                LogFile?.Dispose();
            }
        }

        static ProgramSettings ParseArguments(string[] args)
        {
            var settings = new ProgramSettings();

            // First parse options:
            var index = 0;
            while (index < args.Length && args[index].StartsWith("-"))
            {
                var arg = args[index++];
                switch (arg)
                {
                    case "-subdirs": settings.IncludeSubDirectories = true; break;
                    case "-full": settings.FullRebuild = true; break;
                    case "-subdirremoval": settings.EnableSubDirectoryRemoval = true; break;
                    case "-extract": settings.Extract = true; break;
                    case "-spritesheet": settings.ExtractAsSpriteSheet = true; break;
                    case "-overwrite": settings.OverwriteExistingFiles = true; break;
                    case "-gif": settings.ExtractAsGif = true; break;
                    case "-nologfile": settings.DisableFileLogging = true; break;
                    default: throw new ArgumentException($"Unknown argument: '{arg}'.");
                }
            }

            // Then handle arguments (paths):
            var paths = args.Skip(index).ToArray();
            if (paths.Length == 0)
                throw new ArgumentException("Missing input path (image or sprite file, or folder) argument.");

            if (paths[0].EndsWith(".spr"))
                settings.Extract = true;


            if (settings.Extract)
            {
                // Sprite extraction requires a spr file path or a directory:
                settings.InputPath = args[index++];

                if (index < args.Length)
                {
                    settings.OutputPath = args[index++];
                }
                else
                {
                    var inputIsFile = !string.IsNullOrEmpty(Path.GetExtension(settings.InputPath));
                    if (inputIsFile)
                    {
                        // By default, put the output image(s) in an 'extracted' sub-directory:
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath), "extracted");
                    }
                    else
                    {
                        // By default, put the output images in a '*_extracted' directory next to the input directory:
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath), Path.GetFileNameWithoutExtension(settings.InputPath) + "_extracted");
                    }
                }
            }
            else
            {
                // Sprite building requires an image file path or a directory:
                settings.InputPath = args[index++];

                if (index < args.Length)
                {
                    settings.OutputPath = args[index++];
                }
                else
                {
                    var inputIsFile = !string.IsNullOrEmpty(Path.GetExtension(settings.InputPath));
                    if (inputIsFile)
                    {
                        // By default, put the output sprite in the same directory:
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath), GetSpriteName(settings.InputPath) + ".spr");
                    }
                    else
                    {
                        // By default, put output sprites in a '*_sprites' directory next to the input directory:
                        settings.OutputPath = Path.Combine(Path.GetDirectoryName(settings.InputPath), Path.GetFileNameWithoutExtension(settings.InputPath) + "_sprites");
                    }
                }
            }

            return settings;
        }


        static void ExtractSprites(string inputDirectory, string outputDirectory, bool extractAsSpritesheet, bool overwriteExistingFiles, bool includeSubDirectories)
        {
            throw new NotImplementedException();
        }

        static void ExtractSprite(string inputPath, string outputDirectory, bool extractAsSpritesheet, bool overwriteExistingFiles)
        {
            throw new NotImplementedException();
        }


        static void MakeSprites(string inputDirectory, string outputDirectory, bool fullRebuild, bool includeSubDirectories, bool enableSubDirectoryRemoving)
        {
            var stopwatch = Stopwatch.StartNew();

            (var spritesAdded, var spritesUpdated, var spritesRemoved) = MakeSpritesFromImagesDirectory(inputDirectory, outputDirectory, fullRebuild, includeSubDirectories, enableSubDirectoryRemoving);

            Log($"Updated {outputDirectory} from {inputDirectory}: added {spritesAdded}, updated {spritesUpdated} and removed {spritesRemoved} sprites, in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }

        static void MakeSprite(string inputPath, string outputPath)
        {
            var stopwatch = Stopwatch.StartNew();

            // Gather all related files and settings (for animated sprites, it's possible to use multiple frame-numbered images):
            var inputDirectory = Path.GetDirectoryName(inputPath);
            var spriteName = GetSpriteName(inputPath);
            var spriteMakingSettings = SpriteMakingSettings.Load(inputDirectory, ignoreHistory: true);
            var imagePaths = Directory.EnumerateFiles(inputDirectory)
                .Where(path => GetSpriteName(path) == spriteName)
                .Where(path => ImageReading.IsSupported(path) || spriteMakingSettings.GetSpriteSettings(Path.GetFileName(path)).settings.Converter != null)
                .Where(path => !SpriteMakingSettings.IsConfigurationFile(path))
                .ToArray();

            var conversionOutputDirectory = Path.Combine(inputDirectory, Guid.NewGuid().ToString());
            try
            {
                var success = MakeSprite(spriteName, imagePaths, outputPath, spriteMakingSettings, conversionOutputDirectory, true);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(conversionOutputDirectory))
                        Directory.Delete(conversionOutputDirectory, true);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Failed to delete temporary conversion output directory: {ex.GetType().Name}: '{ex.Message}'.");
                }
            }

            Log($"Created '{outputPath}' (from '{imagePaths.First()}'{(imagePaths.Length > 1 ? $" + {imagePaths.Length - 1} more files" : "")}) in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }


        // Sprite making:
        static (int spritesAdded, int spritesUpdated, int spritesRemoved) MakeSpritesFromImagesDirectory(
            string inputDirectory,
            string outputDirectory,
            bool fullRebuild,
            bool includeSubDirectories,
            bool enableSubDirectoryRemoving)
        {
            var spritesAdded = 0;
            var spritesUpdated = 0;
            var spritesRemoved = 0;

            var spriteMakingSettings = SpriteMakingSettings.Load(inputDirectory);
            var previousFileHashes = LoadFileHashesHistory(inputDirectory);
            var currentFileHashes = new Dictionary<string, byte[]>();
            var conversionOutputDirectory = ExternalConversion.GetConversionOutputDirectory(inputDirectory);

            Directory.CreateDirectory(outputDirectory);

            // Multiple files can map to the same sprite, due to different extensions, filename suffixes and upper/lower-case differences.
            // We'll group files by sprite name, to make these collisions easy to detect:
            var allInputDirectoryFiles = Directory.EnumerateFiles(inputDirectory, "*").ToHashSet();
            var spriteImagePaths = allInputDirectoryFiles
                .Where(path => ImageReading.IsSupported(path) || spriteMakingSettings.GetSpriteSettings(Path.GetFileName(path)).settings.Converter != null)
                .Where(path => !SpriteMakingSettings.IsConfigurationFile(path))
                .GroupBy(path => GetSpriteName(path));

            try
            {
                // Loop over all the groups of input images (each group, if valid, will produce one output sprite):
                foreach (var imagePathsGroup in spriteImagePaths)
                {
                    var spriteName = imagePathsGroup.Key;
                    var outputSpritePath = Path.Combine(outputDirectory, spriteName + ".spr");
                    var isExistingSprite = File.Exists(outputSpritePath);

                    var success = MakeSprite(
                        spriteName,
                        imagePathsGroup,
                        outputSpritePath,
                        spriteMakingSettings,
                        conversionOutputDirectory,
                        fullRebuild,
                        previousFileHashes,
                        currentFileHashes);

                    if (success)
                    {
                        var inputImageCount = imagePathsGroup.Count();
                        if (isExistingSprite)
                        {
                            spritesUpdated += 1;
                            Log($"Updated sprite '{outputSpritePath}' (from '{imagePathsGroup.First()}'{(inputImageCount > 1 ? $" + {inputImageCount - 1} more files" : "")}).");
                        }
                        else
                        {
                            spritesAdded += 1;
                            Log($"Added sprite '{outputSpritePath}' (from '{imagePathsGroup.First()}'{(inputImageCount > 1 ? $" + {inputImageCount - 1} more files" : "")}).");
                        }
                    }
                }

                // Remove sprites whose source images have been removed:
                var oldSpriteNames = previousFileHashes
                    .Select(kv => GetSpriteName(kv.Key))
                    .ToHashSet();
                var newSpriteNames = spriteImagePaths
                    .Select(group => group.Key)
                    .ToHashSet();
                foreach (var spriteName in oldSpriteNames)
                {
                    if (!newSpriteNames.Contains(spriteName))
                    {
                        var spriteFilePath = Path.Combine(outputDirectory, spriteName + ".spr");
                        try
                        {
                            if (File.Exists(spriteFilePath))
                            {
                                File.Delete(spriteFilePath);
                                spritesRemoved += 1;
                                Log($"Removed sprite '{spriteFilePath}'.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"WARNING: Failed to remove '{spriteFilePath}': {ex.GetType().Name}: '{ex.Message}'.");
                        }
                    }
                }

                // Store the most recent hash for each input image, and remember files that have been removed:
                foreach (var filename in previousFileHashes.Keys)
                {
                    if (!currentFileHashes.ContainsKey(filename))
                        currentFileHashes[filename] = null;
                }
                SaveFileHashesHistory(inputDirectory, currentFileHashes);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(conversionOutputDirectory))
                        Directory.Delete(conversionOutputDirectory, true);
                }
                catch (Exception ex)
                {
                    Log($"WARNING: Failed to delete temporary conversion output directory: {ex.GetType().Name}: '{ex.Message}'.");
                }
            }

            // Handle sub-directories (recursively):
            if (includeSubDirectories)
            {
                var previousSubDirectoryNames = LoadSubDirectoriesHistory(inputDirectory);
                var currentSubDirectoryNames = new HashSet<string>();

                foreach (var subDirectoryPath in Directory.EnumerateDirectories(inputDirectory))
                {
                    if (ExternalConversion.IsConversionOutputDirectory(subDirectoryPath))
                        continue;

                    var subDirectoryName = Path.GetFileName(subDirectoryPath);
                    (var added, var updated, var removed) = MakeSpritesFromImagesDirectory(
                        subDirectoryPath,
                        Path.Combine(outputDirectory, subDirectoryName),
                        fullRebuild,
                        includeSubDirectories,
                        enableSubDirectoryRemoving);

                    currentSubDirectoryNames.Add(subDirectoryName);
                    spritesAdded += added;
                    spritesUpdated += updated;
                    spritesRemoved += removed;
                }

                if (enableSubDirectoryRemoving)
                {
                    // Remove output sprites for sub-directories that have been removed:
                    foreach (var subDirectoryName in previousSubDirectoryNames)
                    {
                        // Remove all sprites from the associated output directory, and the directory itself as well if it's empty:
                        if (!currentSubDirectoryNames.Contains(subDirectoryName))
                            spritesRemoved += RemoveOutputSprites(Path.Combine(outputDirectory, subDirectoryName));
                    }
                }

                SaveSubDirectoriesHistory(inputDirectory, previousSubDirectoryNames.Union(currentSubDirectoryNames));
            }

            return (spritesAdded, spritesUpdated, spritesRemoved);
        }

        /// <summary>
        /// Creates and saves a sprite file from the given input images and settings.
        /// If <param name="forceRebuild"/> is false, and <paramref name="previousFileHashes"/> and <paramref name="currentFileHashes"/> are provided,
        /// then this method will skip making a sprite if it already exists and is up-to-date. It will then also update <paramref name="currentFileHashes"/>
        /// with the file hashes of the given input images.
        /// </summary>
        static bool MakeSprite(
            string spriteName,
            IEnumerable<string> imagePaths,
            string outputSpritePath,
            SpriteMakingSettings spriteMakingSettings,
            string conversionOutputDirectory,
            bool forceRebuild,
            IDictionary<string, byte[]> previousFileHashes = null,
            IDictionary<string, byte[]> currentFileHashes = null)
        {
            try
            {
                var imagePathsAndSettings = imagePaths
                    .Select(path =>
                    {
                        var isSupportedFileType = ImageReading.IsSupported(path);
                        return (
                            path,
                            isSupportedFileType,
                            filenameSettings: SpriteFilenameSettings.FromFilename(path),
                            spriteSettings: spriteMakingSettings.GetSpriteSettings(isSupportedFileType ? spriteName : Path.GetFileName(path)));
                    })
                    .OrderBy(file => file.filenameSettings.FrameNumber)
                    .ToArray();

                if (imagePathsAndSettings.Any(file => !file.isSupportedFileType && file.spriteSettings.settings.ConverterArguments == null))
                {
                    Log($"WARNING: some input files for '{spriteName}' are missing converter arguments. Skipping sprite.");
                    return false;
                }
                else if (imagePaths.Count() > 1 && imagePathsAndSettings.Any(file => file.filenameSettings.FrameNumber == null))
                {
                    Log($"WARNING: not all input files for '{spriteName}' contain a frame number ({string.Join(", ", imagePaths)}). Skipping sprite.");
                    return false;
                }

                // Read file hashes - these are used to detect filename changes, and will be stored for future change detection:
                if (currentFileHashes != null && previousFileHashes != null)
                {
                    var imageFileHashes = imagePaths.ToDictionary(Path.GetFileName, GetFileHash);
                    foreach (var kv in imageFileHashes)
                        currentFileHashes[kv.Key] = kv.Value;

                    // Do we need to update this sprite?
                    if (!forceRebuild)
                    {
                        var spriteFileInfo = new FileInfo(outputSpritePath);
                        if (spriteFileInfo.Exists)
                        {
                            var lastSpriteUpdateTime = spriteFileInfo.LastWriteTimeUtc;

                            // Have any settings been updated? Have any source images been updated? Have any frame images been swapped or has any file been renamed?
                            if (!imagePathsAndSettings.Any(file => file.spriteSettings.lastUpdate > lastSpriteUpdateTime) &&
                                !imagePathsAndSettings.Any(file => new FileInfo(file.path).LastWriteTimeUtc > lastSpriteUpdateTime) &&
                                imageFileHashes.All(kv => previousFileHashes.TryGetValue(kv.Key, out var oldHash) && IsEqualHash(oldHash, kv.Value)))
                            {
                                // No changes detected, this sprite doesn't need to be rebuilt:
                                return false;
                            }
                        }
                    }
                }

                // Start building this sprite:
                using (var frameImages = new DisposableList<FrameImage>())
                {
                    foreach (var file in imagePathsAndSettings)
                    {
                        // Do we need to convert this image?
                        var initialImageFilePath = file.path;
                        var imageFilePaths = new[] { initialImageFilePath };
                        var spriteSettings = file.spriteSettings.settings;
                        if (spriteSettings.Converter != null)
                        {
                            if (spriteSettings.ConverterArguments == null)
                                throw new InvalidDataException($"Unable to convert '{file.path}': missing converter arguments.");

                            initialImageFilePath = Path.Combine(conversionOutputDirectory, Path.GetFileNameWithoutExtension(file.path));
                            Directory.CreateDirectory(conversionOutputDirectory);

                            var outputFilePaths = ExternalConversion.ExecuteConversionCommand(spriteSettings.Converter, spriteSettings.ConverterArguments, file.path, initialImageFilePath, Log);
                            if (outputFilePaths.Length < 1)
                                throw new IOException("Unable to find converter output files. Output files must have the same name as the input file (different extensions and suffixes are ok).");

                            imageFilePaths = outputFilePaths.Where(ImageReading.IsSupported).ToArray();
                            if (imageFilePaths.Length < 1)
                                throw new IOException("The converter did not produce any supported file types.");
                        }

                        // Load images (and cut up spritesheets into separate frame images):
                        foreach (var imageFilePath in imageFilePaths)
                        {
                            var image = ImageReading.ReadImage(imageFilePath);
                            if (file.filenameSettings.SpritesheetTileSize is Size tileSize)
                            {
                                if (image.Width % tileSize.Width != 0 || image.Height % tileSize.Height != 0)
                                    throw new InvalidDataException($"Spritesheet image '{file.path}' size ({image.Width} x {image.Height}) is not a multiple of the specified tile size ({tileSize.Width} x {tileSize.Height}).");

                                var tileImages = GetSpritesheetTiles(image, tileSize);
                                foreach (var tileImage in tileImages)
                                    frameImages.Add(new FrameImage(tileImage, file.spriteSettings.settings, frameImages.Count));

                                image.Dispose();
                            }
                            else
                            {
                                frameImages.Add(new FrameImage(image, file.spriteSettings.settings, file.filenameSettings.FrameNumber ?? frameImages.Count));
                            }
                        }
                    }

                    // Sprite settings:
                    var firstFile = imagePathsAndSettings.First();
                    var spriteType = firstFile.filenameSettings.Type ?? firstFile.spriteSettings.settings.SpriteType ?? SpriteType.Parallel;
                    var spriteTextureFormat = firstFile.filenameSettings.TextureFormat ?? firstFile.spriteSettings.settings.SpriteTextureFormat ?? SpriteTextureFormat.Additive;

                    var sprite = CreateSpriteFromImages(frameImages, spriteType, spriteTextureFormat);
                    sprite.Save(outputSpritePath);

                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR: Failed to build '{spriteName}': {ex.GetType().Name}: '{ex.Message}'.");
                return false;
            }
        }

        static int RemoveOutputSprites(string directory)
        {
            if (!Directory.Exists(directory))
                return 0;

            var spritesRemoved = 0;

            // First remove all sprite files:
            foreach (var spriteFilePath in Directory.EnumerateFiles(directory, "*.spr"))
            {
                try
                {
                    File.Delete(spriteFilePath);
                    spritesRemoved += 1;
                }
                catch (Exception ex)
                {
                    Log($"Failed to remove '{spriteFilePath}': {ex.GetType().Name}: '{ex.Message}'.");
                }
            }

            // Then recursively try removing sub-directories:
            foreach (var subDirectoryPath in Directory.EnumerateDirectories(directory))
                spritesRemoved += RemoveOutputSprites(subDirectoryPath);

            try
            {
                // Finally, remove this directory, but only if it's now empty:
                if (!Directory.EnumerateFiles(directory).Any() && !Directory.EnumerateDirectories(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception ex)
            {
                Log($"Failed to remove '{directory}': {ex.GetType().Name}: '{ex.Message}'.");
            }

            return spritesRemoved;
        }

        static string GetSpriteName(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);

            var dotIndex = name.IndexOf('.');
            if (dotIndex >= 0)
                name = name.Substring(0, dotIndex);

            return name.ToLowerInvariant();
        }

        static Image<Rgba32>[] GetSpritesheetTiles(Image<Rgba32> spritesheet, Size tileSize)
        {
            // Frames are taken from left to right, then from top to bottom.
            var frameImages = new List<Image<Rgba32>>();
            for (int y = 0; y + tileSize.Height <= spritesheet.Height; y += tileSize.Height)
            {
                for (int x = 0; x + tileSize.Width <= spritesheet.Width; x += tileSize.Width)
                {
                    var frameImage = spritesheet.Clone(context => context.Crop(new Rectangle(x, y, tileSize.Width, tileSize.Height)));
                    frameImages.Add(frameImage);
                }
            }
            return frameImages.ToArray();
        }

        static Sprite CreateSpriteFromImages(IList<FrameImage> frameImages, SpriteType spriteType, SpriteTextureFormat spriteTextureFormat)
        {
            if (spriteTextureFormat == SpriteTextureFormat.IndexAlpha)
                return CreateIndexAlphaSpriteFromImages(frameImages, spriteType);

            // Create a single color histogram from all frame images:
            var colorHistogram = new Dictionary<Rgba32, int>();
            var isAlphaTest = spriteTextureFormat == SpriteTextureFormat.AlphaTest;
            foreach (var frameImage in frameImages)
            {
                foreach (ImageFrame<Rgba32> frame in frameImage.Image.Frames)
                    ColorQuantization.UpdateColorHistogram(colorHistogram, frame, MakeTransparencyPredicate(frameImage));
            }

            // Create a suitable palette, taking sprite texture format into account:
            var maxColors = isAlphaTest ? 255 : 256;
            var colorClusters = ColorQuantization.GetColorClusters(colorHistogram, maxColors);

            // Always make sure we've got a 256-color palette (some tools can't handle smaller palettes):
            if (colorClusters.Length < maxColors)
            {
                colorClusters = colorClusters
                    .Concat(Enumerable
                        .Range(0, maxColors - colorClusters.Length)
                        .Select(i => (new Rgba32(), new[] { new Rgba32() })))
                    .ToArray();
            }

            if (isAlphaTest)
            {
                var colorKey = new Rgba32(0, 0, 255);
                colorClusters = colorClusters
                    .Append((colorKey, new[] { colorKey }))         // Slot 255: used for transparent pixels
                    .ToArray();
            }


            // Create the actual palette, and a color index lookup cache:
            var palette = colorClusters
                .Select(cluster => cluster.averageColor)
                .ToArray();
            var colorIndexMappingCache = new Dictionary<Rgba32, int>();
            for (int i = 0; i < colorClusters.Length; i++)
            {
                foreach (var color in colorClusters[i].colors)
                    colorIndexMappingCache[color] = i;
            }


            // Create the sprite and its frames:
            var spriteWidth = frameImages.Max(frameImage => frameImage.Image.Frames.OfType<ImageFrame<Rgba32>>().Max(frame => frame.Width));
            var spriteHeight = frameImages.Max(frameImage => frameImage.Image.Frames.OfType<ImageFrame<Rgba32>>().Max(frame => frame.Height));
            var isAnimatedSprite = frameImages.Count() > 1 || frameImages[0].Image.Frames.Count > 1;

            var sprite = Sprite.CreateSprite(spriteType, spriteTextureFormat, spriteWidth, spriteHeight, palette);
            foreach (var frameImage in frameImages)
            {
                var image = frameImage.Image;
                foreach (ImageFrame<Rgba32> frame in image.Frames)
                {
                    sprite.Frames.Add(new Frame {
                        Type = FrameType.Single,
                        FrameOriginX = -(frameImage.Settings.FrameOrigin?.X ?? (frame.Width / 2)),
                        FrameOriginY = frameImage.Settings.FrameOrigin?.Y ?? (frame.Height / 2),
                        FrameWidth = (uint)frame.Width,
                        FrameHeight = (uint)frame.Height,
                        ImageData = CreateFrameImageData(frame, palette, colorIndexMappingCache, frameImage.Settings, MakeTransparencyPredicate(frameImage), disableDithering: isAnimatedSprite),
                    });
                }
            }
            return sprite;


            Func<Rgba32, bool> MakeTransparencyPredicate(FrameImage frameImage)
            {
                var transparencyThreshold = isAlphaTest ? Clamp(frameImage.Settings.AlphaTestTransparencyThreshold ?? 128, 0, 255) : 0;
                if (frameImage.Settings.AlphaTestTransparencyColor is Rgba32 transparencyColor)
                    return color => color.A < transparencyThreshold || (color.R == transparencyColor.R && color.G == transparencyColor.G && color.B == transparencyColor.B);

                return color => color.A < transparencyThreshold;
            }
        }

        static Sprite CreateIndexAlphaSpriteFromImages(IList<FrameImage> frameImages, SpriteType spriteType)
        {
            Rgba32 decalColor;
            if (frameImages.First().Settings.IndexAlphaColor is Rgba32 indexAlphaColor)
            {
                decalColor = indexAlphaColor;
            }
            else
            {
                var colorHistogram = ColorQuantization.GetColorHistogram(frameImages.Select(frameImage => frameImage.Image), color => color.A == 0);
                decalColor = ColorQuantization.GetAverageColor(colorHistogram);
            }
            var palette = Enumerable.Range(0, 255)
                .Select(i => new Rgba32((byte)i, (byte)i, (byte)i))
                .Append(decalColor)
                .ToArray();

            var spriteWidth = frameImages.Max(frameImage => frameImage.Image.Width);
            var spriteHeight = frameImages.Max(frameImage => frameImage.Image.Height);
            var sprite = Sprite.CreateSprite(spriteType, SpriteTextureFormat.IndexAlpha, spriteWidth, spriteHeight, palette);
            foreach (var frameImage in frameImages)
            {
                var mode = frameImage.Settings.IndexAlphaTransparencySource ?? IndexAlphaTransparencySource.AlphaChannel;
                var getPaletteIndex = (mode == IndexAlphaTransparencySource.AlphaChannel) ? (Func<Rgba32, byte>)(color => color.A) :
                                                                                            (Func<Rgba32, byte>)(color => (byte)((color.R + color.G + color.B) / 3));

                var image = frameImage.Image;
                var frame = new Frame {
                    Type = FrameType.Single,
                    FrameOriginX = -(frameImage.Settings.FrameOrigin?.X ?? (image.Width / 2)),
                    FrameOriginY = frameImage.Settings.FrameOrigin?.Y ?? (image.Height / 2),
                    FrameWidth = (uint)image.Width,
                    FrameHeight = (uint)image.Height,
                    ImageData = new byte[image.Width * image.Height],
                };

                for (int y = 0; y < image.Height; y++)
                {
                    var rowSpan = image.GetPixelRowSpan(y);
                    for (int x = 0; x < image.Width; x++)
                    {
                        var color = rowSpan[x];
                        frame.ImageData[y * image.Width + x] = getPaletteIndex(color);
                    }
                }

                sprite.Frames.Add(frame);
            }
            return sprite;
        }

        static byte[] CreateFrameImageData(
            ImageFrame<Rgba32> imageFrame,
            Rgba32[] palette,
            IDictionary<Rgba32, int> colorIndexMappingCache,
            SpriteSettings spriteSettings,
            Func<Rgba32, bool> isTransparent,
            bool disableDithering)
        {
            var getColorIndex = ColorQuantization.CreateColorIndexLookup(palette, colorIndexMappingCache, isTransparent);

            var ditheringAlgorithm = spriteSettings.DitheringAlgorithm ?? (disableDithering ? DitheringAlgorithm.None : DitheringAlgorithm.FloydSteinberg);
            switch (ditheringAlgorithm)
            {
                default:
                case DitheringAlgorithm.None:
                    return ApplyPaletteWithoutDithering();

                case DitheringAlgorithm.FloydSteinberg:
                    return Dithering.FloydSteinberg(imageFrame, palette, getColorIndex, spriteSettings.DitherScale ?? 0.75f, isTransparent);
            }


            byte[] ApplyPaletteWithoutDithering()
            {
                var textureData = new byte[imageFrame.Width * imageFrame.Height];
                for (int y = 0; y < imageFrame.Height; y++)
                {
                    var rowSpan = imageFrame.GetPixelRowSpan(y);
                    for (int x = 0; x < imageFrame.Width; x++)
                    {
                        var color = rowSpan[x];
                        textureData[y * imageFrame.Width + x] = (byte)getColorIndex(color);
                    }
                }
                return textureData;
            }
        }


        /// <summary>
        /// SpriteMaker stores the hash of each source image file.
        /// This enables it to detect filename changes (which cannot be detected by looking at the last modification date).
        /// The names of removed files are also remembered, so only the sprites that were created from those images will be removed,
        /// (otherwise removing sprites could be a very destructive operation, if the output directory already contains other sprites!).
        /// </summary>
        static IDictionary<string, byte[]> LoadFileHashesHistory(string directory)
        {
            var path = GetFileHashesFilePath(directory);
            if (!File.Exists(path))
                return new Dictionary<string, byte[]>();

            return File.ReadAllLines(path)
                .Select(line => line.Split())
                .ToDictionary(
                    parts => HttpUtility.UrlDecode(parts[0]),
                    parts => (parts.Length < 2) ? null : ParseHex(parts[1]));
        }

        static void SaveFileHashesHistory(string directory, IDictionary<string, byte[]> fileHashes)
        {
            File.WriteAllLines(
                GetFileHashesFilePath(directory),
                fileHashes.Select(kv => HttpUtility.UrlEncode(kv.Key) + ((kv.Value == null) ? "" : " " + string.Join("", kv.Value.Select(b => b.ToString("x2"))))));
        }

        static string GetFileHashesFilePath(string directory) => Path.Combine(directory, "filehashes.dat");

        static byte[] GetFileHash(string path)
        {
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha256 = new SHA256Managed())
                return sha256.ComputeHash(file);
        }

        static bool IsEqualHash(byte[] hash1, byte[] hash2) => hash1 != null && hash2 != null && Enumerable.SequenceEqual(hash1, hash2);


        static HashSet<string> LoadSubDirectoriesHistory(string directory)
        {
            var path = GetSubDirectoriesFilePath(directory);
            if (!File.Exists(path))
                return new HashSet<string>();

            return File.ReadAllLines(path)
                .Select(HttpUtility.UrlDecode)
                .ToHashSet();
        }

        static void SaveSubDirectoriesHistory(string directory, IEnumerable<string> directoryNames)
        {
            File.WriteAllLines(
                GetSubDirectoriesFilePath(directory),
                directoryNames
                    .Distinct()
                    .Select(HttpUtility.UrlEncode));
        }

        static string GetSubDirectoriesFilePath(string directory) => Path.Combine(directory, "subdirectories.dat");


        // TODO: Move this to a common place in Shared -- it's duplicated 3 times now!
        static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));

        static byte[] ParseHex(string hexString)
        {
            if (hexString.Length % 2 != 0) throw new InvalidDataException("Hex-string must contain an even number of hexadecimal digits.");

            var bytes = new byte[hexString.Length / 2];
            for (int i = 0; i < hexString.Length; i += 2)
                bytes[i / 2] = byte.Parse(hexString.Substring(i, 2), NumberStyles.HexNumber);

            return bytes;
        }


        static void Log(string message)
        {
            Console.WriteLine(message);
            LogFile?.WriteLine(message);
        }
    }
}
