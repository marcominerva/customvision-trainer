using System;
using System.Collections.Generic;

namespace CustomVisionTrainer.Storage.Collections
{
    public class StorageImage
    {
        public int Id { get; set; }

        public Guid IdCustomVision { get; set; }

        public Guid ProjectId { get; set; }

        public DateTime Created { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public string ImageUri { get; set; }

        public string ThumbnailUri { get; set; }

        public IList<StorageImageTag> Tags { get; set; }

        public string FullFileName { get; set; }
    }
}
