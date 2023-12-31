﻿using TagLib;
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
using static AlbumArtWorkExtractor.Program;
using Force.Crc32;

// Some examples:
//
// -r "E:\Music\MP3's" -t -1 -p "e:\music\album-art\[artist]-[album][disc].jpg" -w 1000 -o
//
//

namespace AlbumArtWorkExtractor
{
    internal class Program
    {
        // A global list of all the errors
        public static ConcurrentBag<ApplicationError> ApplicationErrors = new ConcurrentBag<ApplicationError>();

        public class AlbumArt
        {
            public string Filename { get; set; }
            public byte[] Bytes { get; set; }
            public string CRC { get; set; }
        }

        public class ApplicationError
        {
            public FileInfo fileinfo { get; set; }
            public Exception exception { get; set; }
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

            [Option('n', "nodupes", Required = false, Default = false, HelpText = "Don't create multiple copies of the same image.")]
            public bool NoDupes { get; set; }
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
                AnsiConsole.MarkupLine("\n\nPress [hotpink]any[/] key to exit");
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

        /*
         * Calculate the CRC of the image we want to save.  To try and prevent a bunch of duplicates
         * from being created
         */
        public static string CalcCRC(byte[] data)
        {
            string output = "";

            Crc32Algorithm x = new Crc32Algorithm();

            foreach (byte b in x.ComputeHash(data))
            {
                output += b.ToString("x2");
            }

            return output.ToLower();
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
        private static List<ApplicationError> ProcessPath(DirectoryInfo PathToProcess, Options o)
        {
            // This is the directory we will start in
            List<FileInfo> AudioFiles = PathToProcess.GetFiles($"*.{o.Extension}", SearchOption.TopDirectoryOnly).ToList<FileInfo>();
            List<string> FilesSaved = new List<string>();
            ConcurrentBag<AlbumArt> AlbumArtToSave = new ConcurrentBag<AlbumArt>();
            List<FileAndAudioDetail> FilesAndAudios = new List<FileAndAudioDetail>();
            List<ApplicationError> PathApplicationErrors = new List<ApplicationError>();

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
                        AnsiConsole.MarkupLine($"Analyzing [lightgreen]{file.FullName.EscapeMarkup()}[/]");
                    }

                    try
                    {
                        // Get the tag details of the audio file
                        TagLib.File tagAudioFile = TagLib.File.Create(file.FullName);

                        // keep track of the artists that we've come across
                        if (!String.IsNullOrEmpty(tagAudioFile.Tag.FirstArtist))
                        {
                            if (!UniqueArtists.Contains(tagAudioFile.Tag.FirstArtist))
                                UniqueArtists.Add(tagAudioFile.Tag.FirstArtist);
                        }

                        // we've already processed the info once, so save it here to speed things up
                        FilesAndAudios.Add(new FileAndAudioDetail { Fileinfo = file, AudioDetails = tagAudioFile });
                    }
                    catch (Exception ex)
                    {
                        PathApplicationErrors.Add(new ApplicationError{fileinfo = file, exception = ex});
                    }                    
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
                                FileInfo fi = new FileInfo(TargetImageName);
                                PathApplicationErrors.Add(new ApplicationError {fileinfo = fi, exception = ex});
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
                SaveArtWork(AlbumArtToSave, o);
            }

            // return any errors we encountered
            return PathApplicationErrors;
        }

        /*
         * Saves each piece of album art to disk.  
         */
        public static void SaveArtWork(ConcurrentBag<AlbumArt> AlbumArtToSave, Options o)
        {
            bool WriteFile = false;
            string crc = "";

            foreach (AlbumArt albumart in AlbumArtToSave.OrderBy(aa => aa.Filename))
            {
                try
                {
                    // do we need to resize the image?
                    if (o.Height != null && o.Width != null)
                    {
                        albumart.Bytes = ResizeImage(albumart.Bytes, o.Width, o.Height);
                    }

                    // if we don't want duplicates, we have to calculate the crc of each
                    if (o.NoDupes)
                    {
                        // Calculate the CRC
                        crc = CalcCRC(albumart.Bytes);

                        // find out if we've already written this file
                        int count = AlbumArtToSave.Count(ab => ab.CRC == crc);

                        // If we don't have any files matching the crc, we need to write the file
                        if (count == 0)
                        {
                            WriteFile = true;
                        }
                        else
                        {
                            // update the console user
                            AnsiConsole.MarkupLine($"[blue]Duplicate[/] [yellow]{albumart.Filename.EscapeMarkup()}[/] because of CRC");
                            // Don't write the file
                            WriteFile = false;
                        }
                    }
                    else
                    {
                        WriteFile = true;
                    }

                    // Write the file
                    if (WriteFile)
                    {
                        // save the crc
                        albumart.CRC = crc;

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

                        // set the creation date
                        System.IO.File.SetCreationTime(albumart.Filename, DateTime.Now);

                        // Determine the size of the image
                        System.Drawing.Size size = GetImageDimensions(albumart.Bytes);

                        // update the console user
                        AnsiConsole.MarkupLine($"[lightgreen]Saved[/] [yellow]{albumart.Filename.EscapeMarkup()}[/] [hotpink]({size.Width}x{size.Height})[/]");
                    }
                }
                catch (Exception ex)
                {
                    FileInfo fi = new FileInfo(albumart.Filename);
                    ApplicationErrors.Add(new ApplicationError { fileinfo = fi, exception = ex });
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
            List<DirectoryInfo> AudioPaths = null;

            AnsiConsole.Status()
                .AutoRefresh(true)
                .Spinner(Spinner.Known.BouncingBall)
                .SpinnerStyle(Style.Parse("blue bold"))
                .Start($"Reading root path [hotpink]{o.AudioRootPath}[/]... Please wait", ctx =>
                {
                    ctx.Refresh();
                    AudioPaths = dir.GetDirectories("*.*", SearchOption.AllDirectories).ToList<DirectoryInfo>();
                });

            // show how many files we're processing
            if (o.Verbose)
            {
                AnsiConsole.MarkupLine($"There are [hotpink]{AudioPaths.Count}[/] paths to process.");
            }

            // Set up the parallel options that will be used to determine the thread count
            var parallelOptions = new ParallelOptions()
            {
                MaxDegreeOfParallelism = o.ThreadsToUse
            };

            // Process each file
            Parallel.ForEach(AudioPaths, parallelOptions, path => {
                try
                {
                    // process the next path
                    List<ApplicationError> AudioErrorsInPath = ProcessPath(path, o);

                    // if errors were returned, add them to our global list
                    if (AudioErrorsInPath.Count > 0)
                    {
                        foreach(ApplicationError audioError in AudioErrorsInPath)
                        {
                            ApplicationErrors.Add(audioError);
                        }
                    }
                }
                catch (Exception ex)
                {
                    FileInfo fi = new FileInfo(path.FullName);
                    ApplicationErrors.Add(new ApplicationError { fileinfo = fi, exception = ex });
                }
            });

            // Are there any errors?  If so, display them to the console user
            if (ApplicationErrors.Count > 0)
            {
                Console.Beep();
                int ErrorCount = 0;

                foreach( ApplicationError audioError in ApplicationErrors)
                {
                    AnsiConsole.MarkupLine($"\nError {++ErrorCount} [indianred1]{audioError.exception.Message.EscapeMarkup()}[/]");
                    AnsiConsole.MarkupLine($"[yellow]{audioError.fileinfo.DirectoryName.EscapeMarkup()}\\[/][hotpink]{audioError.fileinfo.Name.EscapeMarkup()}[/]");

                    if (o.Verbose)
                        AnsiConsole.WriteException(audioError.exception);
                }
            }
        }
    }
}