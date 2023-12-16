using TagLib;
using Spectre;
using System.Diagnostics;
using Spectre.Console;

namespace AlbumArtWorkExtractor
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Get the path to scan from the command line
            string RootPath = args[0];
            string ImageNamingFormat = @"[sourcepath]\folder[disc].jpg";

            // Is there a new image format we want to use?
            if (args[1] != null)
            {
                ImageNamingFormat = @"d:\album-art\[artist]-[album].jpg";

                // make sure the path exists
                FileInfo file = new FileInfo(ImageNamingFormat);

                if (!Path.Exists(file.DirectoryName))
                {
                    Directory.CreateDirectory(file.DirectoryName);
                }
            }

            if (!String.IsNullOrEmpty(RootPath) )
            {
                await StartImageExtract(RootPath, ImageNamingFormat);
            }
            else
            {
                AnsiConsole.MarkupLine("Please provide the root path of your audio files.");
            }
            
        }

        private static string? CraftImageFileName(string ImageNamingForamt, FileInfo file, TagLib.File audio)
        {
            string? ImageFileName = ImageNamingForamt;

            if (ImageFileName.Contains("[sourcepath]", StringComparison.OrdinalIgnoreCase))
                ImageFileName = ImageFileName.Replace("[sourcepath]", file.DirectoryName);

            if (ImageFileName.Contains("[disc]", StringComparison.OrdinalIgnoreCase))
            {
                if (audio.Tag.Disc == 0)
                    ImageFileName = ImageFileName.Replace("[disc]", "");
                else
                    ImageFileName = ImageFileName.Replace("[disc]", audio.Tag.Disc.ToString());
            }

            if (ImageFileName.Contains("[artist]", StringComparison.OrdinalIgnoreCase))
            {
                if (!String.IsNullOrEmpty(audio.Tag.FirstAlbumArtist))
                    ImageFileName = ImageFileName.Replace("[artist]", audio.Tag.FirstAlbumArtist);
                else
                    ImageFileName = ImageFileName.Replace("[artist]", "Various");
            }

            if (ImageFileName.Contains("[album]", StringComparison.OrdinalIgnoreCase))
            {
                if (!String.IsNullOrEmpty(audio.Tag.Album))
                    ImageFileName = ImageFileName.Replace("[album]", audio.Tag.Album);
                else
                    ImageFileName = ImageFileName.Replace("[album]", "Unknown");
            }

            // Return the new filename
            return ImageFileName;
        }

        private static async Task StartImageExtract(string RootPath, string ImageNamingFormat)
        {
            List<string> folders = new List<string>();
            
            // This is the directory we will start in
            DirectoryInfo dir = new DirectoryInfo(RootPath);

            List<FileInfo> AudioFiles = dir.GetFiles("*.mp3", SearchOption.AllDirectories).ToList<FileInfo>();

            var parallelOptions = new ParallelOptions()
            {
                MaxDegreeOfParallelism = 1
            };

            Parallel.ForEach(AudioFiles, parallelOptions, file => {
                try
                {
                    string ProcessingFile = $"[yellow]{file.DirectoryName.Replace(RootPath, "").EscapeMarkup()}[/]\\[hotpink]{file.Name.EscapeMarkup()}[/]";

                    // show what we are processing
                    AnsiConsole.MarkupLine(ProcessingFile);

                    // make it an mp3
                    TagLib.File tFile = TagLib.File.Create(file.FullName);

                    // Need a unique image name
                    string imageName = CraftImageFileName(ImageNamingFormat, file, tFile);

                    if (!System.IO.File.Exists(imageName))
                    {
                        if (tFile.Tag.Pictures.Count() > 0)
                        {
                            using (var ms = new MemoryStream(tFile.Tag.Pictures[0].Data.Data))
                            {
                                using (var fs = new FileStream(imageName, FileMode.Create))
                                {
                                    ms.WriteTo(fs);
                                    ms.Close();
                                }
                            }

                            AnsiConsole.MarkupLine($"[hotpink]{imageName.EscapeMarkup()}[/] saved.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.Print($"{ex.Message} <--> {file}");
                }
            });

            //foreach (string file in files)
            //{

            //}

            Console.WriteLine("Press Any Key");
            Console.ReadKey();
        }

    }
}
