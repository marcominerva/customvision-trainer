using CustomVisionTrainer.Extensions;
using CustomVisionTrainer.Storage;
using ImageMagick;
using Microsoft.Cognitive.CustomVision.Training;
using Microsoft.Cognitive.CustomVision.Training.Models;
using Polly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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

        public static string BaseUri = "https://{0}.api.cognitive.microsoft.com/customvision/v1.2/Training";

        public static async Task TrainAsync(ParsingOptions options)
        {
            // Create the Api, passing in the training key
            var trainingApi = new TrainingApi(new Uri(string.Format(BaseUri, options.Region))) { ApiKey = options.TrainingKey };
            var storageImages = new CognitiveServiceTrainerStorage();

            try
            {
                // Checks what kind of operation the user wants to perform.

                if (options.Delete)
                {
                    try
                    {
                        await DeleteImagesAndTagsAsync(options, trainingApi);
                        storageImages.DeleteDatabase();
                        Console.WriteLine("Images and tags successfully deleted.");
                    }
                    catch
                    {
                    }

                    return;
                }

                if (options.ListProjects)
                {
                    Console.Write("Getting Custom Vision projects... ");
                    var projects = await trainingApi.GetProjectsAsync();

                    Console.Write($"{projects.Count} project(s) found.");
                    Console.WriteLine(Environment.NewLine);

                    foreach (var project in projects)
                    {
                        Console.WriteLine(project.Name);
                        Console.WriteLine(project.Id);
                        Console.WriteLine();
                    }

                    return;
                }

                // Performs some checks before image uploading/downloading.
                if (string.IsNullOrWhiteSpace(options.Folder))
                {
                    Console.WriteLine($"Error: folder not specified.");
                    Console.WriteLine();
                    return;
                }

                if (options.ProjectId == Guid.Empty)
                {
                    Console.WriteLine($"Error: project ID not specified.");
                    Console.WriteLine();
                    return;
                }

                var fullFolder = Path.GetFullPath(options.Folder);
                if (!Directory.Exists(fullFolder))
                {
                    Console.WriteLine($"Error: folder \"{fullFolder}\" does not exist.");
                    Console.WriteLine();
                    return;
                }

                if (options.GetImages)
                {
                    var tags = await trainingApi.GetTagsAsync(options.ProjectId);

                    var client = new HttpClient();
                    IList<Image> images = new List<Image>();

                    // The user wants to download images.
                    var skip = 0;
                    while ((images = await trainingApi.GetTaggedImagesAsync(options.ProjectId, take: 50, skip: skip)).Any())
                    {
                        foreach (var image in images)
                        {
                            var tagName = tags.Tags.First(t => t.Id == image.Tags.First().TagId).Name;
                            var folder = Path.Combine(fullFolder, tagName);
                            if (!Directory.Exists(folder))
                            {
                                Console.WriteLine($"Creating folder '{tagName}'...");
                                Directory.CreateDirectory(folder);
                            }

                            var fileName = $"{image.Id}.jpg";
                            Console.WriteLine($"Getting '{fileName}'...");

                            var data = await client.GetByteArrayAsync(image.ImageUri);
                            var fullFileName = Path.Combine(folder, fileName);
                            await File.WriteAllBytesAsync(fullFileName, data);
                        }

                        skip += 50;
                    }

                    Console.WriteLine("Images download successfully.");
                    Console.WriteLine();

                    return;
                }

                // This is the standard upload and tag operation.
                foreach (var dir in Directory.EnumerateDirectories(fullFolder).Where(f => !Path.GetFileName(f).StartsWith("!")))
                {
                    var tagName = Path.GetFileName(dir).ToLower();
                    Console.WriteLine($"\nCheck latest images uploaded '{tagName}'...");

                    IList<Image> imagesAlreadyExists = new List<Image>();
                    IList<Image> imagesAlreadyExistsTmp = new List<Image>();

                    var skip = 0;
                    while ((imagesAlreadyExistsTmp = await trainingApi.GetTaggedImagesAsync(options.ProjectId, tagIds: new[] { tagName }, take: 50, skip: skip)).Any())
                    {
                        foreach (var item in imagesAlreadyExistsTmp)
                        {
                            imagesAlreadyExists.Add(item);
                        }

                        skip += 50;
                    }

                    Console.WriteLine($"\nCreating tag '{tagName}'...");
                    var tagExist = storageImages.FindTag(tagName, options.ProjectId);
                    Tag tag = null;

                    if (tagExist == null)
                    {
                        tag = await trainingApi.CreateTagAsync(options.ProjectId, tagName);
                        storageImages.InsertTag(new Storage.Collections.StorageTag { IdCustomVision = tag.Id, TagName = tag.Name, ProjectId = options.ProjectId });
                    }
                    else
                    {
                        if (trainingApi.GetTag(tagExist.ProjectId, tagExist.IdCustomVision) == null)
                        {
                            await trainingApi.DeleteTagAsync(options.ProjectId, tagExist.IdCustomVision);
                            tag = await trainingApi.CreateTagAsync(options.ProjectId, tagName);
                            storageImages.InsertTag(new Storage.Collections.StorageTag { IdCustomVision = tag.Id, TagName = tag.Name, ProjectId = options.ProjectId });
                        }
                        else
                        {
                            tag = new Tag(tagExist.IdCustomVision, tagName);
                        }
                    }

                    var images = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                               .Where(s => s.EndsWith(".jpg", StringComparison.InvariantCultureIgnoreCase)
                               || s.EndsWith(".jpeg", StringComparison.InvariantCultureIgnoreCase)
                               || s.EndsWith(".png", StringComparison.InvariantCultureIgnoreCase)
                               || s.EndsWith(".bmp", StringComparison.InvariantCultureIgnoreCase)).ToList();

                    var tempImages = new List<ImageDto>();
                    for (var i = 0; i < images.Count; i++)
                    {
                        Stream imageToUpload = null;
                        var image = images.ElementAt(i);
                        var imageName = Path.GetFileName(image);
                        var storageImage = storageImages.FindImage(image, options.ProjectId);

                        if (storageImage == null || !imagesAlreadyExists.Any(x => x.Id == storageImage.IdCustomVision))
                        {
                            // Resizes the image before sending it to the service.
                            using (var input = new MemoryStream(File.ReadAllBytes(image)))
                            {
                                imageToUpload = options.Width.GetValueOrDefault() > 0 || options.Height.GetValueOrDefault() > 0
                                    ? await input.ResizeImageAsync(options.Width.GetValueOrDefault(), options.Height.GetValueOrDefault())
                                    : input;

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
                        if (tempImages.Count % 32 == 0 && tempImages.Any() || (i == images.Count - 1))
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
                var imageFileCreateBatch = new ImageFileCreateBatch
                {
                    Images = new List<ImageFileCreateEntry>()
                };

                foreach (var img in images)
                {
                    imageFileCreateBatch.Images.Add(new ImageFileCreateEntry
                    {
                        Name = img.FullName,
                        TagIds = new[] { img.Tag.Id },
                        Contents = img.Content
                    });

                    Console.WriteLine($"Uploading image {img.FileName}...");
                }

                await Policy
                    .Handle<Exception>()
                    .WaitAndRetryAsync(retries)
                    .ExecuteAsync(async () =>
                    {
                        var reponseCognitiveService = await trainingApi.CreateImagesFromFilesAsync(options.ProjectId, imageFileCreateBatch);
                        if (reponseCognitiveService.Images != null)
                        {
                            for (var i = 0; i < reponseCognitiveService.Images.Count; i++)
                            {
                                var img = reponseCognitiveService.Images.ElementAt(i);

                                // https://docs.microsoft.com/en-us/rest/api/cognitiveservices/customvisiontraining/createimagesfrompredictions/createimagesfrompredictions
                                if ((img.Status == "OK" || img.Status == "OKDuplicate") && img.Image != null)
                                {
                                    var uploadedImage = img.Image;
                                    var tagsToStore = uploadedImage.Tags?.Select(x => new Storage.Collections.StorageImageTag()
                                    {
                                        Created = x.Created,
                                        TagId = x.TagId
                                    }).ToList();

                                    storageImages.InsertImage(new Storage.Collections.StorageImage()
                                    {
                                        ProjectId = options.ProjectId,
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
        }

        private static async Task DeleteImagesAndTagsAsync(ParsingOptions options, TrainingApi trainingApi)
        {
            // Delete all tagged images.
            IList<Image> taggedImages;
            Console.WriteLine("Deleting existing images...");

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
