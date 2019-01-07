using ImageResizer;
using Microsoft.Cognitive.CustomVision.Training;
using Microsoft.Cognitive.CustomVision.Training.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Train
{
    public static class Trainer
    {
        public static async Task TrainAsync(ParsingOptions options)
        {
            // Create the Api, passing in the training key
            var trainingApi = new TrainingApi { ApiKey = options.TrainingKey };

            if (options.Delete)
            {
                try
                {
                    await DeleteImagesAndTagsAsync(options, trainingApi);
                    Console.WriteLine("Images and tags successfully deleted.");
                }
                catch
                {
                }

                return;
            }

            var fullFolder = Path.GetFullPath(options.Folder);
            if (!Directory.Exists(fullFolder))
            {
                Console.WriteLine($"Error: folder \"{fullFolder}\" does not exist.");
                Console.WriteLine(string.Empty);
                return;
            }

            try
            {
                await DeleteImagesAndTagsAsync(options, trainingApi);

                // Creates the string for resized images.
                string resizeString = null;
                if (options.Width.GetValueOrDefault() > 0)
                {
                    resizeString += $"width={options.Width}&";
                }
                if (options.Height.GetValueOrDefault() > 0)
                {
                    resizeString += $"height={options.Height}&";
                }

                if (!string.IsNullOrWhiteSpace(resizeString))
                {
                    resizeString += "crop=auto&scale=both";
                }

                foreach (var dir in Directory.EnumerateDirectories(fullFolder).Where(f => !Path.GetFileName(f).StartsWith("!")))
                {
                    var tagName = Path.GetFileName(dir).ToLower();

                    Console.WriteLine($"\nCreating tag '{tagName}'...");
                    var tag = await trainingApi.CreateTagAsync(options.ProjectId, tagName);

                    var images = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                               .Where(s => s.EndsWith(".jpg", StringComparison.InvariantCultureIgnoreCase)
                               || s.EndsWith(".jpeg", StringComparison.InvariantCultureIgnoreCase)
                               || s.EndsWith(".png", StringComparison.InvariantCultureIgnoreCase)
                               || s.EndsWith(".bmp", StringComparison.InvariantCultureIgnoreCase));

                    foreach (var image in images)
                    {
                        var imageName = Path.GetFileName(image);
                        Console.WriteLine($"Uploading image {imageName}...");

                        // Resizes the image before sending it to the service.
                        using (var input = new MemoryStream(File.ReadAllBytes(image)))
                        {
                            if (!string.IsNullOrWhiteSpace(resizeString))
                            {
                                using (var output = new MemoryStream())
                                {
                                    ImageBuilder.Current.Build(input, output, new ResizeSettings(resizeString));
                                    output.Position = 0;
                                    await trainingApi.CreateImagesFromDataAsync(options.ProjectId, output, new List<string>() { tag.Id.ToString() });
                                }
                            }
                            else
                            {
                                await trainingApi.CreateImagesFromDataAsync(options.ProjectId, input, new List<string>() { tag.Id.ToString() });
                            }
                        }
                    }
                }

                // Now there are images with tags start training the project
                Console.WriteLine("\nTraining...");
                var iteration = await trainingApi.TrainProjectAsync(options.ProjectId);

                // The returned iteration will be in progress, and can be queried periodically to see when it has completed
                while (iteration.Status == "Training")
                {
                    await Task.Delay(1000);

                    // Re-query the iteration to get it's updated status
                    iteration = trainingApi.GetIteration(options.ProjectId, iteration.Id);
                }

                // The iteration is now trained. Make it the default project endpoint
                iteration.IsDefault = true;
                trainingApi.UpdateIteration(options.ProjectId, iteration.Id, iteration);

                Console.WriteLine("Training completed.\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nUnexpected error: {ex.GetBaseException()?.Message}.\n");
            }
        }

        private static async Task DeleteImagesAndTagsAsync(ParsingOptions options, TrainingApi trainingApi)
        {
            // Delete all tagged images.
            Console.WriteLine("Deleting existing images...");
            var taggedImages = await trainingApi.GetTaggedImagesAsync(options.ProjectId);
            await trainingApi.DeleteImagesAsync(options.ProjectId, taggedImages.Select(i => i.Id.ToString()).ToList());

            // Delete all tags.
            Console.WriteLine("Deleting existing tags...");
            var tags = await trainingApi.GetTagsAsync(options.ProjectId);
            foreach (var tag in tags.Tags)
            {
                await trainingApi.DeleteTagAsync(options.ProjectId, tag.Id);
            }
        }
    }
}
