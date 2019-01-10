using CustomVisionTrainer.Storage.Collections;
using LiteDB;
using System;
using System.Threading.Tasks;

namespace CustomVisionTrainer.Storage
{
    public class CognitiveServiceTrainerStorage : IDisposable
    {
        private LiteDatabase db;
        private LiteCollection<StorageImage> _storageCollection;
        private LiteCollection<StorageTag> _tagCollection;
        private readonly object dbLock = new object();

        public CognitiveServiceTrainerStorage()
        {
            db = new LiteDatabase(@"CognitiveServiceTrainerStorage.db");
            
            _storageCollection = db.GetCollection<StorageImage>(nameof(StorageImage));
            _tagCollection = db.GetCollection<StorageTag>(nameof(StorageTag));
        }

        public StorageImage FindImage(string fullFileName, Guid projectId)
        {
            lock (dbLock)
            {
                return _storageCollection.FindOne(x => x.FullFileName == fullFileName && x.ProjectId == projectId);
            }           
        }

        public StorageTag FindTag(string tagName, Guid projectId)
        {
            lock (dbLock)
            {
                return _tagCollection.FindOne(x => x.TagName == tagName && x.ProjectId == projectId);
            }
        }

        public void InsertImage(StorageImage image)
        {
            lock (dbLock)
            {
                _storageCollection.Insert(image);
                _storageCollection.EnsureIndex(x => x.FullFileName);
                _storageCollection.EnsureIndex(x => x.ProjectId);
            }
        }

        public void InsertTag(StorageTag tag)
        {
            lock (dbLock)
            {
                _tagCollection.Insert(tag);
                _tagCollection.EnsureIndex(x => x.TagName);
                _tagCollection.EnsureIndex(x => x.ProjectId);
            }
        }

        public void DeleteDB()
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
