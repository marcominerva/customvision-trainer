using CustomVisionTrainer.Storage;
using ImageMagick;
using Microsoft.Cognitive.CustomVision.Training;
using Microsoft.Cognitive.CustomVision.Training.Models;
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
        public static TimeSpan[] retries = new[] {
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(50),
            TimeSpan.FromSeconds(100),
            TimeSpan.FromSeconds(150),
            TimeSpan.FromSeconds(300),
            TimeSpan.FromSeconds(600)
        };
        public static async Task TrainAsync(ParsingOptions options)
        {
            // Create the Api, passing in the training key
            var trainingApi = new TrainingApi { ApiKey = options.TrainingKey };
            CognitiveServiceTrainerStorage storageImages = new CognitiveServiceTrainerStorage();

            if (options.Delete)
            {
                try
                {
                    await DeleteImagesAndTagsAsync(options, trainingApi);
                    storageImages.DeleteDB();
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
                //await DeleteImagesAndTagsAsync(options, trainingApi);                
                foreach (var dir in Directory.EnumerateDirectories(fullFolder).Where(f => !Path.GetFileName(f).StartsWith("!")))
                {
                    var tagName = Path.GetFileName(dir).ToLower();
                    Console.WriteLine($"\nCheck latest images uploaded '{tagName}'...");
                    IList<Microsoft.Cognitive.CustomVision.Training.Models.Image> imagesAlreadyExists = new List<Microsoft.Cognitive.CustomVision.Training.Models.Image>();
                    IList<Microsoft.Cognitive.CustomVision.Training.Models.Image> imagesAlreadyExistsTmp = new List<Microsoft.Cognitive.CustomVision.Training.Models.Image>();
                    int skip = 0;
                    while ((imagesAlreadyExistsTmp = await trainingApi.GetTaggedImagesAsync(options.ProjectId, tagIds: new[] { tagName}, take: 50, skip: skip)).Any())
                    {
                        skip += 50;
                        foreach (var item in imagesAlreadyExistsTmp)
                        {
                            imagesAlreadyExists.Add(item);
                        }
                    }

                    Console.WriteLine($"\nCreating tag '{tagName}'...");
                    var tagExist = storageImages.FindTag(tagName);
                    Tag tag = null;
                    if (tagExist == null)
                    {
                        tag = await trainingApi.CreateTagAsync(options.ProjectId, tagName);
                        storageImages.InsertTag(new Storage.Collections.StorageTag { IdCustomVision = tag.Id, TagName = tag.Name });
                    }
                    else
                    {
                        if ((await trainingApi.GetTagAsync(options.ProjectId, tagExist.IdCustomVision)) == null)
                        {
                            await trainingApi.DeleteTagAsync(options.ProjectId, tagExist.IdCustomVision);
                            tag = await trainingApi.CreateTagAsync(options.ProjectId, tagName);
                            storageImages.InsertTag(new Storage.Collections.StorageTag { IdCustomVision = tag.Id, TagName = tag.Name });
                        }
                        else
                            tag = new Tag(tagExist.IdCustomVision, tagName);
                    }

                    var images = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                               .Where(s => s.EndsWith(".jpg", StringComparison.InvariantCultureIgnoreCase)
                               || s.EndsWith(".jpeg", StringComparison.InvariantCultureIgnoreCase)
                               || s.EndsWith(".png", StringComparison.InvariantCultureIgnoreCase)
                               || s.EndsWith(".bmp", StringComparison.InvariantCultureIgnoreCase)).ToList();

                    List<ImageDto> tempImages = new List<ImageDto>();
                    for (int i = 0; i < images.Count; i++)
                    {
                        Stream imageToUpload = null;
                        string image = images.ElementAt(i);
                        var imageName = Path.GetFileName(image);
                        var storageImage = storageImages.FindImage(image);
                        if (storageImage == null || !imagesAlreadyExists.Any(x => x.Id == storageImage.IdCustomVision))
                        {
                            // Resizes the image before sending it to the service.
                            using (var input = new MemoryStream(File.ReadAllBytes(image)))
                            {
                                if (options.Width.GetValueOrDefault() > 0 || options.Height.GetValueOrDefault() > 0)
                                    imageToUpload = await ResizeImageAsync(input, options.Width.GetValueOrDefault(), options.Height.GetValueOrDefault());
                                else
                                    imageToUpload = input;
                                
                                tempImages.Add(new ImageDto
                                {
                                    FullName = image,
                                    FileName = imageName,
                                    Content = imageToUpload.ToByteArray(),
                                    Tag = tag
                                });
                                imageToUpload.Dispose();
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Image already exist {imageName}...");
                        }
                        //Persist batch images
                        if (tempImages.Count % 32 == 0 || i == images.Count - 1)
                        {
                            await UploadImagesAsync(tempImages);
                            tempImages.Clear();
                            tempImages.Capacity = 0;
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



            async Task UploadImagesAsync(List<ImageDto> images)
            {
                ImageFileCreateBatch imageFileCreateBatch = new ImageFileCreateBatch();
                imageFileCreateBatch.Images = new List<ImageFileCreateEntry>();
                foreach (var img in images)
                {
                    imageFileCreateBatch.Images.Add(new ImageFileCreateEntry
                    {
                        Name = img.FileName,
                        TagIds = new [] { img.Tag.Id },
                        Contents = img.Content
                    });
                    Console.WriteLine($"Uploading image {img.FileName}...");
                }
                await Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(retries)
                    .ExecuteAsync(async () =>
                    {
                        ImageCreateSummary reponseCognitiveService = await trainingApi.CreateImagesFromFilesAsync(options.ProjectId, imageFileCreateBatch);
                        if (reponseCognitiveService.Images != null)
                        {
                            for (int i = 0; i < reponseCognitiveService.Images.Count; i++)
                            {
                                var img = reponseCognitiveService.Images.ElementAt(i);
                                // https://docs.microsoft.com/en-us/rest/api/cognitiveservices/customvisiontraining/createimagesfrompredictions/createimagesfrompredictions
                                if ((img.Status == "OK" || img.Status == "OKDuplicate") && img.Image != null)
                                {
                                    var uploadedImage = img.Image;
                                    var tagsToStore = uploadedImage.Tags?.Select(x => new Storage.Collections.StorageImageTag() { Created = x.Created, TagId = x.TagId }).ToList();
                                    storageImages.InsertImage(new Storage.Collections.StorageImage()
                                    {
                                        FullFileName = imageFileCreateBatch.Images.ElementAt(i).Name,
                                        IdCustomVision = uploadedImage.Id,
                                        ImageUri = uploadedImage.ImageUri,
                                        Created = uploadedImage.Created,
                                        Height = uploadedImage.Height,
                                        Tags = tagsToStore,
                                        ThumbnailUri = uploadedImage.ThumbnailUri,
                                        Width = uploadedImage.Width
                                    });
                                }
                                else
                                {
                                    Console.WriteLine($"API bad response: {img.Status }");
                                    throw new InvalidOperationException($"API bad response: {img.Status }");
                                }
                            }
                        }
                    });
                await Task.Delay(500);
            }


            async Task UploadImageAsync(Stream input, string imageName, string image, Tag tag)
            {
                ImageCreateSummary reponseCognitiveService;
                if (input.Position > 0)
                    input.Position = 0;

                reponseCognitiveService = await trainingApi.CreateImagesFromDataAsync(options.ProjectId, input, new List<string>() { tag.Id.ToString() });
                if (reponseCognitiveService.Images != null)
                {
                    foreach (var img in reponseCognitiveService.Images)
                    {
                        // https://docs.microsoft.com/en-us/rest/api/cognitiveservices/customvisiontraining/createimagesfrompredictions/createimagesfrompredictions
                        if ((img.Status == "OK" || img.Status == "OKDuplicate") && img.Image != null)
                        {
                            Console.WriteLine($"Uploaded image {imageName}...");
                            var uploadedImage = img.Image;
                            var tagsToStore = uploadedImage.Tags != null
                                ? uploadedImage.Tags.Select(x => new Storage.Collections.StorageImageTag() { Created = x.Created, TagId = x.TagId }).ToList()
                                : null;
                            storageImages.InsertImage(new Storage.Collections.StorageImage()
                            {
                                FullFileName = image,
                                IdCustomVision = uploadedImage.Id,
                                ImageUri = uploadedImage.ImageUri,
                                Created = uploadedImage.Created,
                                Height = uploadedImage.Height,
                                Tags = tagsToStore,
                                ThumbnailUri = uploadedImage.ThumbnailUri,
                                Width = uploadedImage.Width
                            });
                        }
                        else
                        {
                            Console.WriteLine($"API bad response: {img.Status }");
                            throw new InvalidOperationException($"API bad response: {img.Status }");
                        }
                    }
                }
            }
        }

        public static byte[] ToByteArray(this Stream input)
        {
            input.Position = 0;
            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                return ms.ToArray();
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
