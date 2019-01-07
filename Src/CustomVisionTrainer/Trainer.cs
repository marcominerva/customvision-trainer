using ImageMagick;
using Microsoft.Cognitive.CustomVision.Training;
using Polly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CustomVisionTrainer
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
                foreach (var dir in Directory.EnumerateDirectories(fullFolder).Where(f => !Path.GetFileName(f).StartsWith("!")))
                {
                    var tagName = Path.GetFileName(dir).ToLower();

                    Console.WriteLine($"\nCreating tag '{tagName}'...");
                    var tag = await trainingApi.CreateTagAsync(options.ProjectId, tagName);

                    var images = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                               .Where(s => s.EndsWith(".jpg", StringComparison.InvariantCultureIgnoreCase)
                               || s.EndsWith(".jpeg", StringComparison.InvariantCultureIgnoreCase)
                               || s.EndsWith(".png", StringComparison.InvariantCultureIgnoreCase)
                               || s.EndsWith(".bmp", StringComparison.InvariantCultureIgnoreCase)).ToList();

                    Parallel.ForEach(images, async (image) =>
                    {
                        var imageName = Path.GetFileName(image);
                        Console.WriteLine($"Uploading image {imageName}...");

                        // Resizes the image before sending it to the service.
                        using (var input = new MemoryStream(File.ReadAllBytes(image)))
                        {
                            var retries = new[] {
                                TimeSpan.FromSeconds(1),
                                TimeSpan.FromSeconds(2),
                                TimeSpan.FromSeconds(3),
                                TimeSpan.FromSeconds(5),
                                TimeSpan.FromSeconds(10),
                                TimeSpan.FromSeconds(15),
                                TimeSpan.FromSeconds(30),
                                TimeSpan.FromSeconds(60)
                            };

                            if (options.Width.GetValueOrDefault() > 0 || options.Height.GetValueOrDefault() > 0)
                            {
                                using (var output = await ResizeImageAsync(input, options.Width.GetValueOrDefault(), options.Height.GetValueOrDefault()))
                                {
                                    await Policy
                                       .Handle<Exception>()
                                       .WaitAndRetryAsync(retries)
                                       .ExecuteAsync(async () =>
                                       {
                                           await trainingApi.CreateImagesFromDataAsync(options.ProjectId, output, new List<string>() { tag.Id.ToString() });
                                       });
                                }
                            }
                            else
                            {
                                await Policy
                                    .Handle<Exception>()
                                    .WaitAndRetryAsync(retries)
                                    .ExecuteAsync(async () =>
                                    {
                                        await trainingApi.CreateImagesFromDataAsync(options.ProjectId, input, new List<string>() { tag.Id.ToString() });
                                    });
                            }
                        }
                    });
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

        private static Task<Stream> ResizeImageAsync(Stream image, int width, int height)
        {
            // Read image from stream
            using (var output = new MagickImage(image))
            {
                // The image will be resized to fit inside the specified size.
                var size = new MagickGeometry(width, height)
                {
                    Greater = true
                };
                output.Resize(size);

                var ms = new MemoryStream();
                output.Write(ms);
                ms.Position = 0;

                return Task.FromResult(ms as Stream);
            }
        }

        private static async Task DeleteImagesAndTagsAsync(ParsingOptions options, TrainingApi trainingApi)
        {
            // Delete all tagged images.
            Console.WriteLine("Deleting existing images...");
            IList<Microsoft.Cognitive.CustomVision.Training.Models.Image> taggedImages;
            while ((taggedImages = await trainingApi.GetTaggedImagesAsync(options.ProjectId, take: 50, skip: 0)).Any())
            {
                await trainingApi.DeleteImagesAsync(options.ProjectId, taggedImages.Select(i => i.Id.ToString()).ToList());
            }

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
