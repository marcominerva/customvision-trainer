using System;
using System.Text;

namespace CustomVisionTrainer.Storage.Collections
{
    public class StorageTag
    {
        public Guid ProjectId { get; set; }

        public int Id { get; set; }

        public Guid IdCustomVision { get; set; }

        public string TagName { get; set; }
    }
}
