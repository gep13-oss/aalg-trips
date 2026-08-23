using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AalgTrips.Models
{
    /// <summary>
    /// An <see cref="IPhotoStore"/> backed by an Azure Blob Storage container.
    /// Album content is stored under the same key layout the local store uses
    /// (<c>{albumId}/data.json</c>, <c>{albumId}/{photo}</c>,
    /// <c>{albumId}/thumbnail/{thumb}</c>, and a top-level
    /// <c>markers.json</c>), so content is decoupled from the app and survives
    /// redeploys. Photos are served directly from the container — or a CDN in
    /// front of it — via the public URLs this store returns, rather than being
    /// proxied through the app. The container is created on start-up with
    /// blob-level public read access so those URLs resolve.
    /// </summary>
    public sealed class AzureBlobPhotoStore : IPhotoStore
    {
        private readonly BlobContainerClient _container;

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureBlobPhotoStore"/>
        /// class and ensures the backing container exists. The container is
        /// created private (no public access): content is only reachable by a
        /// signed-in user through the app's authenticated media endpoint, never by
        /// a direct blob URL.
        /// </summary>
        /// <param name="connectionString">The storage account connection string.</param>
        /// <param name="containerName">The container album content is stored in.</param>
        public AzureBlobPhotoStore(string connectionString, string containerName)
        {
            var service = new BlobServiceClient(connectionString);
            _container = service.GetBlobContainerClient(containerName);
            _container.CreateIfNotExists(PublicAccessType.None);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListAlbumIds()
        {
            var ids = new List<string>();

            foreach (var item in _container.GetBlobsByHierarchy(BlobTraits.None, BlobStates.None, "/", null, default))
            {
                if (item.IsPrefix)
                {
                    string id = item.Prefix.TrimEnd('/');

                    // Journey content lives under a top-level "journeys/" prefix; it is
                    // a separate catalogue, so keep it out of the album listing.
                    if (!id.Equals(PhotoStoreConventions.JourneysFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        ids.Add(id);
                    }
                }
            }

            return ids;
        }

        /// <inheritdoc />
        public AlbumMetaData TryReadMetadata(string albumId)
        {
            var blob = _container.GetBlobClient(MetadataKey(albumId));

            if (!blob.Exists())
            {
                return null;
            }

            BlobDownloadResult download = blob.DownloadContent();
            return JsonSerializer.Deserialize<AlbumMetaData>(download.Content.ToString());
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListPhotoFileNames(string albumId)
        {
            string prefix = albumId + "/";
            var names = new List<string>();

            // A hierarchical listing returns the blobs directly under the album
            // (data.json and the photos) and a prefix for the thumbnail folder;
            // taking the blobs and keeping the image files yields the originals.
            foreach (var item in _container.GetBlobsByHierarchy(BlobTraits.None, BlobStates.None, "/", prefix, default))
            {
                if (item.IsBlob)
                {
                    string name = item.Blob.Name.Substring(prefix.Length);
                    if (PhotoStoreConventions.IsImageFile(name))
                    {
                        names.Add(name);
                    }
                }
            }

            return names;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListThumbnailFileNames(string albumId)
        {
            string prefix = ThumbnailPrefix(albumId);

            return _container.GetBlobs(BlobTraits.None, BlobStates.None, prefix, default)
                .Select(b => b.Name.Substring(prefix.Length))
                .ToList();
        }

        /// <inheritdoc />
        public bool AlbumExists(string albumId)
        {
            return _container.GetBlobs(BlobTraits.None, BlobStates.None, albumId + "/", default).Any();
        }

        /// <inheritdoc />
        public bool PhotoExists(string albumId, string fileName)
        {
            return _container.GetBlobClient(PhotoKey(albumId, fileName)).Exists();
        }

        /// <inheritdoc />
        public async Task WriteMetadataAsync(string albumId, AlbumMetaData metadata)
        {
            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(stream, metadata);
            stream.Position = 0;
            await _container.GetBlobClient(MetadataKey(albumId)).UploadAsync(stream, overwrite: true);
        }

        /// <inheritdoc />
        public async Task DeleteAlbumAsync(string albumId)
        {
            foreach (var blob in _container.GetBlobs(BlobTraits.None, BlobStates.None, albumId + "/", default).ToList())
            {
                await _container.GetBlobClient(blob.Name).DeleteIfExistsAsync();
            }
        }

        /// <inheritdoc />
        public async Task SavePhotoAsync(string albumId, string fileName, Stream content)
        {
            await _container.GetBlobClient(PhotoKey(albumId, fileName)).UploadAsync(content, overwrite: true);
        }

        /// <inheritdoc />
        public Stream OpenPhoto(string albumId, string fileName)
        {
            return _container.GetBlobClient(PhotoKey(albumId, fileName)).OpenRead();
        }

        /// <inheritdoc />
        public async Task SaveThumbnailAsync(string albumId, string thumbnailFileName, Stream content)
        {
            await _container.GetBlobClient(ThumbnailKey(albumId, thumbnailFileName)).UploadAsync(content, overwrite: true);
        }

        /// <inheritdoc />
        public async Task DeletePhotoAsync(string albumId, string fileName)
        {
            await _container.GetBlobClient(PhotoKey(albumId, fileName)).DeleteIfExistsAsync();

            string prefix = ThumbnailPrefix(albumId);

            foreach (var blob in _container.GetBlobs(BlobTraits.None, BlobStates.None, prefix, default).ToList())
            {
                string name = blob.Name.Substring(prefix.Length);
                if (PhotoStoreConventions.ThumbnailBelongsTo(name, fileName))
                {
                    await _container.GetBlobClient(blob.Name).DeleteIfExistsAsync();
                }
            }
        }

        /// <inheritdoc />
        public async Task RenamePhotoAsync(string albumId, string oldFileName, string newFileName)
        {
            await CopyBlobAsync(PhotoKey(albumId, oldFileName), PhotoKey(albumId, newFileName));
            await _container.GetBlobClient(PhotoKey(albumId, oldFileName)).DeleteIfExistsAsync();

            string prefix = ThumbnailPrefix(albumId);

            foreach (var blob in _container.GetBlobs(BlobTraits.None, BlobStates.None, prefix, default).ToList())
            {
                string name = blob.Name.Substring(prefix.Length);
                if (PhotoStoreConventions.ThumbnailBelongsTo(name, oldFileName))
                {
                    string renamed = PhotoStoreConventions.RenameThumbnail(name, oldFileName, newFileName);
                    await CopyBlobAsync(blob.Name, prefix + renamed);
                    await _container.GetBlobClient(blob.Name).DeleteIfExistsAsync();
                }
            }
        }

        /// <inheritdoc />
        public async Task RenameAlbumAsync(string oldAlbumId, string newAlbumId)
        {
            string sourcePrefix = oldAlbumId + "/";
            string destinationPrefix = newAlbumId + "/";

            // Blob storage has no folder move, so each blob under the album's prefix
            // is server-side copied to the new prefix and then removed. The listing
            // is materialised first so deleting a source blob does not disturb the
            // enumeration.
            foreach (var blob in _container.GetBlobs(BlobTraits.None, BlobStates.None, sourcePrefix, default).ToList())
            {
                string destination = destinationPrefix + blob.Name.Substring(sourcePrefix.Length);
                await CopyBlobAsync(blob.Name, destination);
                await _container.GetBlobClient(blob.Name).DeleteIfExistsAsync();
            }
        }

        /// <inheritdoc />
        public async Task WriteMarkersAsync(IEnumerable<Marker> markers)
        {
            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(stream, markers);
            stream.Position = 0;
            await _container.GetBlobClient(PhotoStoreConventions.MarkersFileName).UploadAsync(stream, overwrite: true);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListJourneyIds()
        {
            var ids = new List<string>();
            string prefix = JourneysPrefix();

            foreach (var item in _container.GetBlobsByHierarchy(BlobTraits.None, BlobStates.None, "/", prefix, default))
            {
                if (item.IsPrefix)
                {
                    // item.Prefix is "journeys/{journeyId}/"; take the id between them.
                    ids.Add(item.Prefix.Substring(prefix.Length).TrimEnd('/'));
                }
            }

            return ids;
        }

        /// <inheritdoc />
        public JourneyMetaData TryReadJourney(string journeyId)
        {
            var blob = _container.GetBlobClient(JourneyMetadataKey(journeyId));

            if (!blob.Exists())
            {
                return null;
            }

            BlobDownloadResult download = blob.DownloadContent();
            return JsonSerializer.Deserialize<JourneyMetaData>(download.Content.ToString());
        }

        /// <inheritdoc />
        public bool JourneyExists(string journeyId)
        {
            return _container.GetBlobs(BlobTraits.None, BlobStates.None, JourneyPrefix(journeyId), default).Any();
        }

        /// <inheritdoc />
        public async Task WriteJourneyAsync(string journeyId, JourneyMetaData metadata)
        {
            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(stream, metadata);
            stream.Position = 0;
            await _container.GetBlobClient(JourneyMetadataKey(journeyId)).UploadAsync(stream, overwrite: true);
        }

        /// <inheritdoc />
        public async Task DeleteJourneyAsync(string journeyId)
        {
            foreach (var blob in _container.GetBlobs(BlobTraits.None, BlobStates.None, JourneyPrefix(journeyId), default).ToList())
            {
                await _container.GetBlobClient(blob.Name).DeleteIfExistsAsync();
            }
        }

        /// <inheritdoc />
        public async Task RenameJourneyAsync(string oldJourneyId, string newJourneyId)
        {
            string sourcePrefix = JourneyPrefix(oldJourneyId);
            string destinationPrefix = JourneyPrefix(newJourneyId);

            // Blob storage has no folder move, so each blob under the journey's prefix
            // is server-side copied to the new prefix and then removed. The listing
            // is materialised first so deleting a source blob does not disturb the
            // enumeration.
            foreach (var blob in _container.GetBlobs(BlobTraits.None, BlobStates.None, sourcePrefix, default).ToList())
            {
                string destination = destinationPrefix + blob.Name.Substring(sourcePrefix.Length);
                await CopyBlobAsync(blob.Name, destination);
                await _container.GetBlobClient(blob.Name).DeleteIfExistsAsync();
            }
        }

        /// <inheritdoc />
        public async Task WriteJourneysAsync(IEnumerable<JourneyRoute> routes)
        {
            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(stream, routes);
            stream.Position = 0;
            await _container.GetBlobClient(PhotoStoreConventions.JourneysFileName).UploadAsync(stream, overwrite: true);
        }

        /// <inheritdoc />
        public IReadOnlyList<RouteSegment> TryReadJourneyRoute(string journeyId)
        {
            var blob = _container.GetBlobClient(JourneyRouteKey(journeyId));

            if (!blob.Exists())
            {
                return null;
            }

            BlobDownloadResult download = blob.DownloadContent();
            return RouteSegment.FromStoredJson(download.Content.ToString());
        }

        /// <inheritdoc />
        public async Task SaveJourneyRouteAsync(string journeyId, IEnumerable<RouteSegment> route)
        {
            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(stream, route);
            stream.Position = 0;
            await _container.GetBlobClient(JourneyRouteKey(journeyId)).UploadAsync(stream, overwrite: true);
        }

        /// <inheritdoc />
        public async Task DeleteJourneyRouteAsync(string journeyId)
        {
            await _container.GetBlobClient(JourneyRouteKey(journeyId)).DeleteIfExistsAsync();
        }

        /// <inheritdoc />
        public bool TryOpenContent(string key, out Stream content)
        {
            content = null;

            var blob = _container.GetBlobClient(key);

            if (!blob.Exists())
            {
                return false;
            }

            content = blob.OpenRead();
            return true;
        }

        /// <inheritdoc />
        public string PhotoUrl(string albumId, string fileName)
        {
            return PhotoStoreConventions.PhotoUrl(albumId, fileName);
        }

        /// <inheritdoc />
        public string ThumbnailUrl(string albumId, string thumbnailFileName)
        {
            return PhotoStoreConventions.ThumbnailUrl(albumId, thumbnailFileName);
        }

        /// <inheritdoc />
        public string MarkersUrl()
        {
            return PhotoStoreConventions.MarkersUrl();
        }

        /// <inheritdoc />
        public string JourneysUrl()
        {
            return PhotoStoreConventions.JourneysUrl();
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListJourneyPhotoFileNames(string journeyId, string stopKey)
        {
            string prefix = JourneyStopPrefix(journeyId, stopKey);
            var names = new List<string>();

            // A hierarchical listing returns the photos directly under the stop and a
            // prefix for the thumbnail folder; taking the blobs and keeping the image
            // files yields the originals.
            foreach (var item in _container.GetBlobsByHierarchy(BlobTraits.None, BlobStates.None, "/", prefix, default))
            {
                if (item.IsBlob)
                {
                    string name = item.Blob.Name.Substring(prefix.Length);
                    if (PhotoStoreConventions.IsImageFile(name))
                    {
                        names.Add(name);
                    }
                }
            }

            return names;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListJourneyThumbnailFileNames(string journeyId, string stopKey)
        {
            string prefix = JourneyStopThumbnailPrefix(journeyId, stopKey);

            return _container.GetBlobs(BlobTraits.None, BlobStates.None, prefix, default)
                .Select(b => b.Name.Substring(prefix.Length))
                .ToList();
        }

        /// <inheritdoc />
        public bool JourneyPhotoExists(string journeyId, string stopKey, string fileName)
        {
            return _container.GetBlobClient(JourneyPhotoKey(journeyId, stopKey, fileName)).Exists();
        }

        /// <inheritdoc />
        public async Task SaveJourneyPhotoAsync(string journeyId, string stopKey, string fileName, Stream content)
        {
            await _container.GetBlobClient(JourneyPhotoKey(journeyId, stopKey, fileName)).UploadAsync(content, overwrite: true);
        }

        /// <inheritdoc />
        public Stream OpenJourneyPhoto(string journeyId, string stopKey, string fileName)
        {
            return _container.GetBlobClient(JourneyPhotoKey(journeyId, stopKey, fileName)).OpenRead();
        }

        /// <inheritdoc />
        public async Task SaveJourneyThumbnailAsync(string journeyId, string stopKey, string thumbnailFileName, Stream content)
        {
            await _container.GetBlobClient(JourneyThumbnailKey(journeyId, stopKey, thumbnailFileName)).UploadAsync(content, overwrite: true);
        }

        /// <inheritdoc />
        public async Task DeleteJourneyPhotoAsync(string journeyId, string stopKey, string fileName)
        {
            await _container.GetBlobClient(JourneyPhotoKey(journeyId, stopKey, fileName)).DeleteIfExistsAsync();

            string prefix = JourneyStopThumbnailPrefix(journeyId, stopKey);

            foreach (var blob in _container.GetBlobs(BlobTraits.None, BlobStates.None, prefix, default).ToList())
            {
                string name = blob.Name.Substring(prefix.Length);
                if (PhotoStoreConventions.ThumbnailBelongsTo(name, fileName))
                {
                    await _container.GetBlobClient(blob.Name).DeleteIfExistsAsync();
                }
            }
        }

        /// <inheritdoc />
        public string JourneyPhotoUrl(string journeyId, string stopKey, string fileName)
        {
            return PhotoStoreConventions.JourneyPhotoUrl(journeyId, stopKey, fileName);
        }

        /// <inheritdoc />
        public string JourneyThumbnailUrl(string journeyId, string stopKey, string thumbnailFileName)
        {
            return PhotoStoreConventions.JourneyThumbnailUrl(journeyId, stopKey, thumbnailFileName);
        }

        private static string MetadataKey(string albumId)
        {
            return $"{albumId}/{PhotoStoreConventions.MetadataFileName}";
        }

        private static string JourneysPrefix()
        {
            return $"{PhotoStoreConventions.JourneysFolder}/";
        }

        private static string JourneyPrefix(string journeyId)
        {
            return $"{PhotoStoreConventions.JourneysFolder}/{journeyId}/";
        }

        private static string JourneyMetadataKey(string journeyId)
        {
            return $"{PhotoStoreConventions.JourneysFolder}/{journeyId}/{PhotoStoreConventions.JourneyMetadataFileName}";
        }

        private static string JourneyRouteKey(string journeyId)
        {
            return $"{PhotoStoreConventions.JourneysFolder}/{journeyId}/{PhotoStoreConventions.JourneyRouteFileName}";
        }

        private static string JourneyStopPrefix(string journeyId, string stopKey)
        {
            return $"{PhotoStoreConventions.JourneysFolder}/{journeyId}/{stopKey}/";
        }

        private static string JourneyStopThumbnailPrefix(string journeyId, string stopKey)
        {
            return $"{PhotoStoreConventions.JourneysFolder}/{journeyId}/{stopKey}/{PhotoStoreConventions.ThumbnailFolder}/";
        }

        private static string JourneyPhotoKey(string journeyId, string stopKey, string fileName)
        {
            return $"{PhotoStoreConventions.JourneysFolder}/{journeyId}/{stopKey}/{fileName}";
        }

        private static string JourneyThumbnailKey(string journeyId, string stopKey, string thumbnailFileName)
        {
            return $"{PhotoStoreConventions.JourneysFolder}/{journeyId}/{stopKey}/{PhotoStoreConventions.ThumbnailFolder}/{thumbnailFileName}";
        }

        private static string PhotoKey(string albumId, string fileName)
        {
            return $"{albumId}/{fileName}";
        }

        private static string ThumbnailPrefix(string albumId)
        {
            return $"{albumId}/{PhotoStoreConventions.ThumbnailFolder}/";
        }

        private static string ThumbnailKey(string albumId, string thumbnailFileName)
        {
            return $"{albumId}/{PhotoStoreConventions.ThumbnailFolder}/{thumbnailFileName}";
        }

        private async Task CopyBlobAsync(string sourceKey, string destinationKey)
        {
            using var stream = await _container.GetBlobClient(sourceKey).OpenReadAsync();
            await _container.GetBlobClient(destinationKey).UploadAsync(stream, overwrite: true);
        }
    }
}