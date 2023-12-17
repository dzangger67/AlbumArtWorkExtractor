**AlbumArtWorkExtractor** is a simple C# console application that will allow you to extract all the album art work from your music collection.  It was originally designed to extract the album art work to each folder where the audio files reside.  You can now provide your own naming pattern so you can have all the art work exported to a single folder with a naming convention of your choice.  The naming of the files is only as good as the data in your audio tags.  Only a few tags like artist, album, disc are supported.  This can easily be changed in the code itself.  Or request the new features in discussions.

The default file naming pattern is:

    [sourcepath]\\folder[disc].jpg

**[sourcepath]** is the path to the audio files so all  images will be stored along with the audio files.  
If you have all your audio files in a single folder, then this probably isn't for you.  This assumes you have an organized audio collection.  If the audio files are part of a multi-disc collection, then an image will be written for each **[disc]**.  If the disc number doesn't exist, then this value will be blank.  If this folder contains music from a 2 disc collection,  you can expect to see folder01.jpg and folder02.jpg as long as your audio files are tagged properly.  This is done because some multi-disc collections have different art work per disc.  Even if they're not, you should at least end up with folder.jpg.  

A future enhancement might be added that can check for duplicate images to prevent wasted space.

### Some examples

`AlbumArtWorkExtractor -r "E:\Music\MP3's" -t -1 -p "e:\music\album-art\[artist]-[album][disc].jpg" -w 1000 -o`<br /> 
|Option|Description|
|--|--|
|-r|The root path of where your audio files are|
|-t|Number of threads to use; default is -1 (use all available)|
|-p|This is the naming pattern for the album art work. Currently only sourcepath, artist, album and disc are available. New tags can easily be added. You can request them in Discussions.|
|-w|Resize the image with a width of 1000.  The height will be similar since all images will maintain their aspect ratio.|
|-o|Overwrite any image that exists.|


