using TagLib;
using Spectre;
using System.Diagnostics;
using Spectre.Console;
using CommandLine;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Some examples:
//
// -r "E:\Music\MP3's" -t -1 -p "e:\music\album-art\[artist]-[album][disc].jpg" -w 1000 -o
//
//

namespace AlbumArtWorkExtractor
{
    internal class Program
    {
        public class AlbumArt
        {
            public string Filename { get; set; }
            public byte[] Bytes { get; set; }
        }

        public class FileAndAudioDetail
        {
            public FileInfo Fileinfo { get; set; }
            public TagLib.File AudioDetails { get; set; }
        }

        public class Options
        {
            [Option('r', "root", Required = true, HelpText = "The root path to your audio files")]
            public string AudioRootPath { get; set; }

            [Option('e', "ext", Required = false, Default = "mp3", HelpText = "The file extension of the audio files to scan.")]
            public string Extension{ get; set; }

            [Option('t', "threads", Required = false, Default = -1, HelpText = "The number of threads to use. -1 is all threads")]
            public int ThreadsToUse { get; set; }
            
            [Option('p', "pattern", Required = false, Default = @"[sourcepath]\\folder[disc].jpg", HelpText = "This is the naming pattern to use for the album art.")]
            public string ImageNamingPattern { get; set; }
            //
            // "e:\music\album-art\[artist]-[album][disc].jpg"
            //

            [Option('v', "verbose", Required = false, Default = false, HelpText = "Be verbose with what's happening.")]
            public bool Verbose { get; set; }
            
            [Option('o', "overwrite", Required = false, Default = false, HelpText = "Overwrite the file if it already exists.")]
            public bool Overwrite{ get; set; }

            [Option('h', "height", Required = false, Default = null, HelpText = "The new Height for the image.")]
            public int? Height { get; set; }

            [Option('w', "width", Required = false, Default = null, HelpText = "The new Width for the image.")]
            public int? Width { get; set; }

        }

        static async Task Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args).WithParsed<Options>(async o =>
            { 
                // Make sure any target folder exists
                if (!o.ImageNamingPattern.Contains("[sourcepath]", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        // make sure the path exists.  If to another drive, then it should be at the 
                        // beginning of the image format.
                        FileInfo file = new FileInfo(o.ImageNamingPattern);

                        if (!Path.Exists(file.DirectoryName))
                        {
                            Directory.CreateDirectory(file.DirectoryName);
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.WriteException(ex);
                        Environment.Exit(1);
                    }                    
                }

                // Make sure if we're resizing the image, the values make sense.  If only one value is
                // provided, set the other value to the other value .. LOL
                if (o.Width != null && o.Height == null)
                    o.Height = o.Width;
                else if (o.Height != null && o.Width == null)
                    o.Width = o.Height;

                // Start the image extraction from all the audio files
                await StartImageExtract(o);
            });

            // If we're in the IDE, prompt the user to exit so we can see the results
            if (IsInIDE())
            {
                AnsiConsole.MarkupLine("Press [hotpink]Any[/] key to exit");
                Console.ReadKey();
            }
        }

        /*
         * Some artists or song titles might have invalid characters.  They
         * have to be stripped before we write them
         */
        private static string RemoveInvalidCharacters(string filename)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();

            foreach (char c in invalidChars)
            {
                filename = filename.Replace(c.ToString(), "_");
            }

            return filename;
        }

        /*
         * Gets the dimensions of the image
         */
        public static System.Drawing.Size GetImageDimensions(byte[] imageBytes)
        {
            using (var ms = new System.IO.MemoryStream(imageBytes))
            {
                using (var img = System.Drawing.Image.FromStream(ms))
                {
                    return img.Size;
                }
            }
        }

        /*
         * Resize an image if the user wants them the same size.  From co-pilot
         * 
         * byte[] resizedImageBytes = ResizeImage(myImageBytes, 640, 480);
         */
        public static byte[] ResizeImage(byte[] imageBytes, int? maxWidth, int? maxHeight)
        {
            using (var ms = new MemoryStream(imageBytes))
            {
                using (var img = Image.FromStream(ms))
                {
                    var ratioX = (double)maxWidth / img.Width;
                    var ratioY = (double)maxHeight / img.Height;
                    var ratio = Math.Min(ratioX, ratioY);

                    var newWidth = (int)(img.Width * ratio);
                    var newHeight = (int)(img.Height * ratio);

                    Rectangle destRect = new Rectangle(0, 0, newWidth, newHeight);
                    Bitmap destImage = new Bitmap(newWidth, newHeight);

                    destImage.SetResolution(img.HorizontalResolution, img.VerticalResolution);

                    using (Graphics graphics = Graphics.FromImage(destImage))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.CompositingQuality = CompositingQuality.HighQuality;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.SmoothingMode = SmoothingMode.HighQuality;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                        using (var wrapMode = new ImageAttributes())
                        {
                            wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                            graphics.DrawImage(img, destRect, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, wrapMode);
                        }
                    }

                    using (MemoryStream ms2 = new MemoryStream())
                    {
                        destImage.Save(ms2, img.RawFormat);
                        return ms2.ToArray();
                    }
                }
            }
        }

        private static string? CraftImageFileName(string ImageNamingForamt, FileInfo file, TagLib.File audio, bool UniqueArtist = true)
        {
            string? ImageFileName = ImageNamingForamt;

            if (ImageFileName.Contains("[sourcepath]", StringComparison.OrdinalIgnoreCase))
                ImageFileName = ImageFileName.Replace("[sourcepath]", file.DirectoryName);

            if (ImageFileName.Contains("[disc]", StringComparison.OrdinalIgnoreCase))
            {
                if (audio.Tag.Disc == 0)
                    ImageFileName = ImageFileName.Replace("[disc]", "");
                else
                    ImageFileName = ImageFileName.Replace("[disc]", audio.Tag.Disc.ToString("0#"));
            }

            if (ImageFileName.Contains("[artist]", StringComparison.OrdinalIgnoreCase))
            {
                if (!String.IsNullOrEmpty(audio.Tag.FirstArtist) && UniqueArtist)
                    ImageFileName = ImageFileName.Replace("[artist]", RemoveInvalidCharacters(audio.Tag.FirstArtist));
                else
                    ImageFileName = ImageFileName.Replace("[artist]", "Various");
            }

            if (ImageFileName.Contains("[album]", StringComparison.OrdinalIgnoreCase))
            {
                if (!String.IsNullOrEmpty(audio.Tag.Album))
                    ImageFileName = ImageFileName.Replace("[album]", RemoveInvalidCharacters(audio.Tag.Album));
                else
                    ImageFileName = ImageFileName.Replace("[album]", "Unknown");
            }

            // Return the new file name
            return ImageFileName;
        }

        /*
         * Process the path of audio files.  
         */
        private static void ProcessPath(DirectoryInfo PathToProcess, Options o)
        {
            // This is the directory we will start in
            List<FileInfo> AudioFiles = PathToProcess.GetFiles($"*.{o.Extension}", SearchOption.TopDirectoryOnly).ToList<FileInfo>();
            List<string> FilesSaved = new List<string>();
            ConcurrentBag<AlbumArt> AlbumArtToSave = new ConcurrentBag<AlbumArt>();
            List<FileAndAudioDetail> FilesAndAudios = new List<FileAndAudioDetail>();

            // Show the path we are processing
            AnsiConsole.MarkupLine($"Scanning [yellow]{PathToProcess.FullName.EscapeMarkup()}[/]");

            // only do something if there were some audio files found
            if (AudioFiles.Count > 0)
            {
                List<string> UniqueArtists = new List<string>();

                // Need to determine the artists... is it various artists or a single artist?
                foreach (FileInfo file in AudioFiles)
                {
                    if (o.Verbose)
                    {
                        AnsiConsole.MarkupLine($"Processing [lightgreen]{file.FullName.EscapeMarkup()}[/]");
                    }

                    // Get the tag details of the audio file
                    TagLib.File tFile = TagLib.File.Create(file.FullName);

                    // keep track of the artists that we've come across
                    if (!String.IsNullOrEmpty(tFile.Tag.FirstArtist))
                    {
                         if (!UniqueArtists.Contains(tFile.Tag.FirstArtist))
                            UniqueArtists.Add(tFile.Tag.FirstArtist);
                    }

                    // we've already processed the info once, so save it here to speed things up
                    FilesAndAudios.Add(new FileAndAudioDetail {Fileinfo = file, AudioDetails = tFile}) ;
                }

                // process each audio file we found
                foreach (FileAndAudioDetail details in FilesAndAudios)
                {
                    // Need a unique image name
                    string TargetImageName = CraftImageFileName(o.ImageNamingPattern, details.Fileinfo, details.AudioDetails, UniqueArtists.Count == 1);

                    // if we haven't seen this file yet, go ahead and process it
                    if (!FilesSaved.Contains(TargetImageName))
                    {
                        // we've seen it now
                        FilesSaved.Add(TargetImageName);

                        // check to see if the file already exists.  if so, add it to the list and/or check the overwrite flag
                        bool FileExists = System.IO.File.Exists(TargetImageName);

                        // if the file exists and we want to overwrite, try to delete the file
                        if (FileExists && o.Overwrite)
                        {
                            // if the file exists and we want to overwrite it, delete it first; the first time only
                            try
                            {
                                // Delete the file
                                System.IO.File.Delete(TargetImageName);
                                // Update the console user
                                AnsiConsole.MarkupLine($"[indianred1]Deleted[/] [yellow]{TargetImageName.EscapeMarkup()}[/] because of Overwrite flag");
                                // It no longer exists because we deleted it
                                FileExists = false;
                            }
                            catch (Exception ex)
                            {
                                AnsiConsole.WriteException(ex);
                            }
                        }

                        // Make sure there is a picture to extract
                        if (!FileExists && details.AudioDetails.Tag.Pictures.Count() > 0)
                        {
                            AlbumArt aa = new AlbumArt();
                            aa.Filename = TargetImageName;
                            aa.Bytes = details.AudioDetails.Tag.Pictures[0].Data.Data;
                            AlbumArtToSave.Add(aa);

                            // Keep track that we wrote this file
                            FilesSaved.Add(TargetImageName);
                        }
                    }
                }

                // now save the image files we need to write
                foreach(AlbumArt albumart in AlbumArtToSave)
                {
                    try
                    {
                        // do we need to resize the image?
                        if (o.Height != null && o.Width != null)
                        {
                            albumart.Bytes = ResizeImage(albumart.Bytes, o.Width, o.Height);
                        }

                        // Now write the bytes to a file
                        using (var ms = new MemoryStream(albumart.Bytes))
                        {
                            using (var fs = new FileStream(albumart.Filename, FileMode.Create))
                            {
                                ms.WriteTo(fs);
                                ms.Flush();
                                ms.Close();
                            }
                        }

                        // Determine the size of the image
                        System.Drawing.Size size = GetImageDimensions(albumart.Bytes);

                        // update the console user
                        AnsiConsole.MarkupLine($"[lightgreen]Saved[/] [yellow]{albumart.Filename.EscapeMarkup()}[/] [hotpink]({size.Width}x{size.Height})[/]");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.WriteException(ex);
                    }                    
                }
            }
        }

        /*
         * Are we in the IDE or not?
         */
        private static bool IsInIDE()
        {
            return System.Diagnostics.Debugger.IsAttached;
        }

        private static async Task StartImageExtract(Options o)
        {
            // This is the directory we will start in
            DirectoryInfo dir = new DirectoryInfo(o.AudioRootPath);

            List<DirectoryInfo> AudioPaths = dir.GetDirectories("*.*", SearchOption.AllDirectories).ToList<DirectoryInfo>();

            // show how many files we're processing
            AnsiConsole.MarkupLine($"There are [hotpink]{AudioPaths.Count}[/] paths to process.");

            // Set up the parallel options that will be used to determine the thread count
            var parallelOptions = new ParallelOptions()
            {
                MaxDegreeOfParallelism = o.ThreadsToUse
            };

            // Process each file
            Parallel.ForEach(AudioPaths, parallelOptions, path => {
                try
                {
                    // show what we are processing
                    if (o.Verbose)
                    {
                        string ProcessingPath = $"[yellow]{path.FullName.Replace(o.AudioRootPath, "").EscapeMarkup()}[/]";
                        AnsiConsole.MarkupLine(ProcessingPath);
                    }

                    // process the next path
                    ProcessPath(path, o);
                }
                catch (Exception ex)
                {
                    //ImageNamingFormat = @"d:\album-art\[artist]-[album].jpg";
                    AnsiConsole.MarkupLine($"Error processing: {path.FullName.EscapeMarkup()}");
                    AnsiConsole.WriteException(ex);
                }
            });
        }
    }
}
