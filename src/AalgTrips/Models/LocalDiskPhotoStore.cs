using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AalgTrips.Models
{
    /// <summary>
    /// An <see cref="IPhotoStore"/> backed by the local filesystem: albums are
    /// folders under a web-served <c>albums</c> root, photos are files inside
    /// them, and thumbnails live in a <c>thumbnail</c> sub-folder. This is the
    /// development and test store, and preserves the site's original on-disk
    /// layout and <c>/albums/…</c> URLs exactly, so behaviour is unchanged when
    /// it is selected. Content is served as static files by the app.
    /// </summary>
    public sealed class LocalDiskPhotoStore : IPhotoStore
    {
        private readonly string _root;

        /// <summary>
        /// Initializes a new instance of the <see cref="LocalDiskPhotoStore"/>
        /// class over the given albums root directory. The directory need not
        /// exist yet; it is created on first write.
        /// </summary>
        /// <param name="albumsRoot">The absolute path of the <c>albums</c> root.</param>
        public LocalDiskPhotoStore(string albumsRoot)
        {
            _root = albumsRoot;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListAlbumIds()
        {
            if (!Directory.Exists(_root))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateDirectories(_root)
                .Select(d => new DirectoryInfo(d).Name)
                .Where(name => !name.Equals(PhotoStoreConventions.CruisesFolder, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <inheritdoc />
        public AlbumMetaData TryReadMetadata(string albumId)
        {
            string metadataPath = Path.Combine(AlbumDir(albumId), PhotoStoreConventions.MetadataFileName);

            if (!File.Exists(metadataPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<AlbumMetaData>(File.ReadAllText(metadataPath));
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListPhotoFileNames(string albumId)
        {
            string dir = AlbumDir(albumId);

            if (!Directory.Exists(dir))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(dir)
                .Select(Path.GetFileName)
                .Where(PhotoStoreConventions.IsImageFile)
                .ToList();
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListThumbnailFileNames(string albumId)
        {
            string dir = Path.Combine(AlbumDir(albumId), PhotoStoreConventions.ThumbnailFolder);

            if (!Directory.Exists(dir))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(dir)
                .Select(Path.GetFileName)
                .ToList();
        }

        /// <inheritdoc />
        public bool AlbumExists(string albumId)
        {
            return Directory.Exists(AlbumDir(albumId));
        }

        /// <inheritdoc />
        public bool PhotoExists(string albumId, string fileName)
        {
            return File.Exists(PhotoPath(albumId, fileName));
        }

        /// <inheritdoc />
        public async Task WriteMetadataAsync(string albumId, AlbumMetaData metadata)
        {
            string dir = AlbumDir(albumId);
            Directory.CreateDirectory(dir);

            using var stream = File.Create(Path.Combine(dir, PhotoStoreConventions.MetadataFileName));
            await JsonSerializer.SerializeAsync(stream, metadata);
        }

        /// <inheritdoc />
        public Task DeleteAlbumAsync(string albumId)
        {
            string dir = AlbumDir(albumId);

            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task SavePhotoAsync(string albumId, string fileName, Stream content)
        {
            string dir = AlbumDir(albumId);
            Directory.CreateDirectory(dir);

            using var file = new FileStream(PhotoPath(albumId, fileName), FileMode.Create, FileAccess.Write);
            await content.CopyToAsync(file);
        }

        /// <inheritdoc />
        public Stream OpenPhoto(string albumId, string fileName)
        {
            return File.OpenRead(PhotoPath(albumId, fileName));
        }

        /// <inheritdoc />
        public async Task SaveThumbnailAsync(string albumId, string thumbnailFileName, Stream content)
        {
            string dir = Path.Combine(AlbumDir(albumId), PhotoStoreConventions.ThumbnailFolder);
            Directory.CreateDirectory(dir);

            string path = SafeCombine(dir, thumbnailFileName);

            using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
            await content.CopyToAsync(file);
        }

        /// <inheritdoc />
        public Task DeletePhotoAsync(string albumId, string fileName)
        {
            string path = PhotoPath(albumId, fileName);

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            string thumbnailDir = Path.Combine(AlbumDir(albumId), PhotoStoreConventions.ThumbnailFolder);

            if (Directory.Exists(thumbnailDir))
            {
                foreach (var thumbnail in Directory.EnumerateFiles(thumbnailDir)
                    .Where(t => PhotoStoreConventions.ThumbnailBelongsTo(Path.GetFileName(t), fileName)))
                {
                    File.Delete(thumbnail);
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RenamePhotoAsync(string albumId, string oldFileName, string newFileName)
        {
            File.Move(PhotoPath(albumId, oldFileName), PhotoPath(albumId, newFileName));

            string thumbnailDir = Path.Combine(AlbumDir(albumId), PhotoStoreConventions.ThumbnailFolder);

            if (Directory.Exists(thumbnailDir))
            {
                foreach (var thumbnail in Directory.EnumerateFiles(thumbnailDir)
                    .Where(t => PhotoStoreConventions.ThumbnailBelongsTo(Path.GetFileName(t), oldFileName))
                    .ToList())
                {
                    string current = Path.GetFileName(thumbnail);
                    string renamed = PhotoStoreConventions.RenameThumbnail(current, oldFileName, newFileName);
                    File.Move(thumbnail, SafeCombine(thumbnailDir, renamed));
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RenameAlbumAsync(string oldAlbumId, string newAlbumId)
        {
            string source = AlbumDir(oldAlbumId);
            string destination = AlbumDir(newAlbumId);

            if (Directory.Exists(source))
            {
                Directory.Move(source, destination);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task WriteMarkersAsync(IEnumerable<Marker> markers)
        {
            Directory.CreateDirectory(_root);

            using var stream = File.Create(Path.Combine(_root, PhotoStoreConventions.MarkersFileName));
            await JsonSerializer.SerializeAsync(stream, markers);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListCruiseIds()
        {
            string cruisesRoot = CruisesRoot();

            if (!Directory.Exists(cruisesRoot))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateDirectories(cruisesRoot)
                .Select(d => new DirectoryInfo(d).Name)
                .ToList();
        }

        /// <inheritdoc />
        public CruiseMetaData TryReadCruise(string cruiseId)
        {
            string metadataPath = Path.Combine(CruiseDir(cruiseId), PhotoStoreConventions.CruiseMetadataFileName);

            if (!File.Exists(metadataPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CruiseMetaData>(File.ReadAllText(metadataPath));
        }

        /// <inheritdoc />
        public bool CruiseExists(string cruiseId)
        {
            return Directory.Exists(CruiseDir(cruiseId));
        }

        /// <inheritdoc />
        public async Task WriteCruiseAsync(string cruiseId, CruiseMetaData metadata)
        {
            string dir = CruiseDir(cruiseId);
            Directory.CreateDirectory(dir);

            using var stream = File.Create(Path.Combine(dir, PhotoStoreConventions.CruiseMetadataFileName));
            await JsonSerializer.SerializeAsync(stream, metadata);
        }

        /// <inheritdoc />
        public Task DeleteCruiseAsync(string cruiseId)
        {
            string dir = CruiseDir(cruiseId);

            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RenameCruiseAsync(string oldCruiseId, string newCruiseId)
        {
            string source = CruiseDir(oldCruiseId);
            string destination = CruiseDir(newCruiseId);

            if (Directory.Exists(source))
            {
                Directory.Move(source, destination);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task WriteCruisesAsync(IEnumerable<CruiseRoute> routes)
        {
            Directory.CreateDirectory(_root);

            using var stream = File.Create(Path.Combine(_root, PhotoStoreConventions.CruisesFileName));
            await JsonSerializer.SerializeAsync(stream, routes);
        }

        /// <inheritdoc />
        public bool TryOpenContent(string key, out Stream content)
        {
            content = null;

            // The key uses '/' separators; map it onto the albums root and confirm
            // the resolved path stays inside it before opening anything.
            string relative = key.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(Path.Combine(_root, relative));

            string baseWithSeparator = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!File.Exists(fullPath))
            {
                return false;
            }

            content = File.OpenRead(fullPath);
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
        public string CruisesUrl()
        {
            return PhotoStoreConventions.CruisesUrl();
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListCruisePhotoFileNames(string cruiseId, string stopKey)
        {
            string dir = CruiseStopDir(cruiseId, stopKey);

            if (!Directory.Exists(dir))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(dir)
                .Select(Path.GetFileName)
                .Where(PhotoStoreConventions.IsImageFile)
                .ToList();
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListCruiseThumbnailFileNames(string cruiseId, string stopKey)
        {
            string dir = Path.Combine(CruiseStopDir(cruiseId, stopKey), PhotoStoreConventions.ThumbnailFolder);

            if (!Directory.Exists(dir))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(dir)
                .Select(Path.GetFileName)
                .ToList();
        }

        /// <inheritdoc />
        public bool CruisePhotoExists(string cruiseId, string stopKey, string fileName)
        {
            return File.Exists(CruisePhotoPath(cruiseId, stopKey, fileName));
        }

        /// <inheritdoc />
        public async Task SaveCruisePhotoAsync(string cruiseId, string stopKey, string fileName, Stream content)
        {
            string dir = CruiseStopDir(cruiseId, stopKey);
            Directory.CreateDirectory(dir);

            using var file = new FileStream(CruisePhotoPath(cruiseId, stopKey, fileName), FileMode.Create, FileAccess.Write);
            await content.CopyToAsync(file);
        }

        /// <inheritdoc />
        public Stream OpenCruisePhoto(string cruiseId, string stopKey, string fileName)
        {
            return File.OpenRead(CruisePhotoPath(cruiseId, stopKey, fileName));
        }

        /// <inheritdoc />
        public async Task SaveCruiseThumbnailAsync(string cruiseId, string stopKey, string thumbnailFileName, Stream content)
        {
            string dir = Path.Combine(CruiseStopDir(cruiseId, stopKey), PhotoStoreConventions.ThumbnailFolder);
            Directory.CreateDirectory(dir);

            string path = SafeCombine(dir, thumbnailFileName);

            using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
            await content.CopyToAsync(file);
        }

        /// <inheritdoc />
        public Task DeleteCruisePhotoAsync(string cruiseId, string stopKey, string fileName)
        {
            string path = CruisePhotoPath(cruiseId, stopKey, fileName);

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            string thumbnailDir = Path.Combine(CruiseStopDir(cruiseId, stopKey), PhotoStoreConventions.ThumbnailFolder);

            if (Directory.Exists(thumbnailDir))
            {
                foreach (var thumbnail in Directory.EnumerateFiles(thumbnailDir)
                    .Where(t => PhotoStoreConventions.ThumbnailBelongsTo(Path.GetFileName(t), fileName)))
                {
                    File.Delete(thumbnail);
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public string CruisePhotoUrl(string cruiseId, string stopKey, string fileName)
        {
            return PhotoStoreConventions.CruisePhotoUrl(cruiseId, stopKey, fileName);
        }

        /// <inheritdoc />
        public string CruiseThumbnailUrl(string cruiseId, string stopKey, string thumbnailFileName)
        {
            return PhotoStoreConventions.CruiseThumbnailUrl(cruiseId, stopKey, thumbnailFileName);
        }

        private string AlbumDir(string albumId)
        {
            return SafeCombine(_root, albumId);
        }

        private string CruisesRoot()
        {
            return Path.Combine(_root, PhotoStoreConventions.CruisesFolder);
        }

        private string CruiseDir(string cruiseId)
        {
            return SafeCombine(CruisesRoot(), cruiseId);
        }

        private string CruiseStopDir(string cruiseId, string stopKey)
        {
            return SafeCombine(CruiseDir(cruiseId), stopKey);
        }

        private string CruisePhotoPath(string cruiseId, string stopKey, string fileName)
        {
            return SafeCombine(CruiseStopDir(cruiseId, stopKey), fileName);
        }

        private string PhotoPath(string albumId, string fileName)
        {
            return SafeCombine(AlbumDir(albumId), fileName);
        }

        // Defence in depth: album ids and file names originate from user input
        // (route values, form fields, uploaded file names). The handlers already
        // validate them, but the store refuses any segment that would escape its
        // base directory so a bug upstream cannot turn into a path traversal.
        private static string SafeCombine(string baseDirectory, string segment)
        {
            if (!SafePathHelper.TryCombineWithin(baseDirectory, segment, out string fullPath))
            {
                throw new ArgumentException($"'{segment}' is not a valid single path segment.", nameof(segment));
            }

            return fullPath;
        }
    }
}