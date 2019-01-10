using System;
using System.Collections.Generic;
using System.Text;

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

    public class StorageImageTag
    {
        public Guid TagId { get; set; }
        public DateTime Created { get; set; }
    }

    public class StorageTag
    {
        public Guid ProjectId { get; set; }
        public int Id { get; set; }
        public Guid IdCustomVision { get; set; }
        public string TagName { get; set; }
    }
}
