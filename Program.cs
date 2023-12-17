using TagLib;
using Spectre;
using System.Diagnostics;
using Spectre.Console;
using CommandLine;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.IO;

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

                // Start the image extraction from all the audio files
                await StartImageExtract(o);
            });

            Console.ReadKey();
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
            List<FileInfo> AudioFiles = PathToProcess.GetFiles("*.mp3", SearchOption.TopDirectoryOnly).ToList<FileInfo>();
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

                    // make it an mp3
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

                        if (FileExists)
                        {
                            // if the file exists and we want to overwrite it, delete it first; the first time only
                            if (o.Overwrite)
                            {
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
                                    Debug.Write(ex.Message);
                                }
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
                foreach(AlbumArt aa in AlbumArtToSave)
                {
                    try
                    {
                        using (var ms = new MemoryStream(aa.Bytes))
                        {
                            using (var fs = new FileStream(aa.Filename, FileMode.Create))
                            {
                                ms.WriteTo(fs);
                                ms.Flush();
                                ms.Close();

                                // update the console user
                                AnsiConsole.MarkupLine($"[lightgreen]Saved[/] [yellow]{aa.Filename.EscapeMarkup()}[/]");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                    }                    
                }
            }
        }

        private static async Task StartImageExtract(Options o)
        {
            // This is the directory we will start in
            DirectoryInfo dir = new DirectoryInfo(o.AudioRootPath);

            List<DirectoryInfo> AudioPaths = dir.GetDirectories("*.*", SearchOption.AllDirectories).ToList<DirectoryInfo>();

            //List<FileInfo> AudioFiles = dir.GetFiles("*.mp3", SearchOption.AllDirectories).ToList<FileInfo>();

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
                    AnsiConsole.MarkupLine($"Error while processing: {path.FullName.EscapeMarkup()}");
                    AnsiConsole.WriteException(ex);
                    Debug.Write(ex.Message);
                }
            });

            Console.WriteLine("Press Any Key");
            Console.ReadKey();
        }
    }
}
