using CustomVisionTrainer.Storage.Collections;
using LiteDB;
using System;
using System.Threading.Tasks;

namespace CustomVisionTrainer.Storage
{
    public class CognitiveServiceTrainerStorage : IDisposable
    {
        private LiteDatabase db;
        private LiteCollection<StorageImage> storageCollection;
        private LiteCollection<StorageTag> tagCollection;
        private readonly object dbLock = new object();

        public CognitiveServiceTrainerStorage()
        {
            db = new LiteDatabase(@"CognitiveServiceTrainerStorage.db");

            storageCollection = db.GetCollection<StorageImage>(nameof(StorageImage));
            tagCollection = db.GetCollection<StorageTag>(nameof(StorageTag));
        }

        public StorageImage FindImage(string fullFileName, Guid projectId)
        {
            lock (dbLock)
            {
                return storageCollection.FindOne(x => x.FullFileName == fullFileName && x.ProjectId == projectId);
            }
        }

        public StorageTag FindTag(string tagName, Guid projectId)
        {
            lock (dbLock)
            {
                return tagCollection.FindOne(x => x.TagName == tagName && x.ProjectId == projectId);
            }
        }

        public void InsertImage(StorageImage image)
        {
            lock (dbLock)
            {
                storageCollection.Insert(image);
                storageCollection.EnsureIndex(x => x.FullFileName);
                storageCollection.EnsureIndex(x => x.ProjectId);
            }
        }

        public void InsertTag(StorageTag tag)
        {
            lock (dbLock)
            {
                tagCollection.Insert(tag);
                tagCollection.EnsureIndex(x => x.TagName);
                tagCollection.EnsureIndex(x => x.ProjectId);
            }
        }

        public void DeleteAllProjectEntries(Guid projectId)
        {
            storageCollection.Delete(x => x.ProjectId == projectId);
            tagCollection.Delete(x => x.ProjectId == projectId);
        }

        public void DeleteAllDatabases()
        {
            foreach (var item in db.FileStorage.FindAll())
            {
                db.FileStorage.Delete(item.Id);
            }
        }

        public void Dispose()
        {
            db.Dispose();
        }
    }
}
