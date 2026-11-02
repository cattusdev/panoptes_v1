using System.Net;
using System.Runtime.CompilerServices;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Panoptes_v1.Models.Media;
using Microsoft.Extensions.Options;

namespace Panoptes_v1.Services.Media
{
    // Cloudflare R2 (ή οποιοδήποτε S3-compatible). Δύο buckets: το public σερβίρεται απευθείας από το R2/CDN
    // (PublicBaseUrl), το private μόνο μέσα από την εφαρμογή μετά τον έλεγχο δικαιωμάτων.
    public sealed class S3MediaStorage : IMediaStorage, IDisposable
    {
        private readonly AmazonS3Client _s3;
        private readonly S3StorageOptions _options;
        private readonly bool _https;

        public S3MediaStorage(IOptions<MediaOptions> options)
        {
            _options = options.Value.S3;
            _s3 = CreateClient(_options);
            _https = _options.ServiceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        public static AmazonS3Client CreateClient(S3StorageOptions options) => new(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,
                AuthenticationRegion = options.Region,
                ForcePathStyle = options.ForcePathStyle,
                // Το SDK v4 στέλνει από προεπιλογή checksums (CRC32 trailers) που το R2 δεν δέχεται παντού
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });

        public async Task WriteAsync(MediaVisibility area, string key, Stream content, CancellationToken cancellationToken = default)
        {
            EnsureValid(key);
            var request = new PutObjectRequest
            {
                BucketName = Bucket(area),
                Key = key,
                InputStream = content,
                AutoCloseStream = false,
                ContentType = MediaDelivery.ContentTypeFor(key),
                DisablePayloadSigning = _https, // Το R2 δεν υποστηρίζει το streaming SigV4 (μόνο πάνω από HTTPS επιτρέπεται)
                DisableDefaultChecksumValidation = true,
            };
            // Ό,τι μπορεί να κρατήσει το ίδιο το αντικείμενο (για το public bucket, που σερβίρεται χωρίς εμάς)
            request.Headers.CacheControl = MediaDelivery.PublicCache;
            if (MediaDelivery.DispositionFor(key) is string disposition)
                request.Headers.ContentDisposition = disposition;

            await _s3.PutObjectAsync(request, cancellationToken);
        }

        // Copy + delete (το S3 δεν έχει rename)· αν το αρχείο δεν υπάρχει στην αφετηρία, δεν γίνεται τίποτα
        public async Task MoveAsync(string key, MediaVisibility from, MediaVisibility to, CancellationToken cancellationToken = default)
        {
            EnsureValid(key);
            if (from == to) return;
            try
            {
                await _s3.CopyObjectAsync(new CopyObjectRequest
                {
                    SourceBucket = Bucket(from),
                    SourceKey = key,
                    DestinationBucket = Bucket(to),
                    DestinationKey = key,
                }, cancellationToken);
            }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }
            await _s3.DeleteObjectAsync(Bucket(from), key, cancellationToken);
        }

        public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            EnsureValid(key);
            await _s3.DeleteObjectAsync(_options.PublicBucket, key, cancellationToken);
            await _s3.DeleteObjectAsync(_options.PrivateBucket, key, cancellationToken);
        }

        public async Task<bool> ExistsAsync(MediaVisibility area, string key, CancellationToken cancellationToken = default)
        {
            EnsureValid(key);
            try
            {
                await _s3.GetObjectMetadataAsync(Bucket(area), key, cancellationToken);
                return true;
            }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public async IAsyncEnumerable<StoredFile> ListAsync(MediaVisibility area, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var request = new ListObjectsV2Request { BucketName = Bucket(area) };
            while (true)
            {
                var page = await _s3.ListObjectsV2Async(request, cancellationToken);
                foreach (var item in page.S3Objects ?? [])
                {
                    // Χωρίς ημερομηνία το θεωρούμε καινούριο (ο καθαρισμός δεν το σβήνει)
                    if (MediaKeys.IsValid(item.Key))
                        yield return new StoredFile(item.Key, item.LastModified?.ToUniversalTime() ?? DateTime.UtcNow);
                }

                if (page.IsTruncated != true) yield break;
                request.ContinuationToken = page.NextContinuationToken;
            }
        }

        public string PublicUrl(string key) => $"{_options.PublicBaseUrl.TrimEnd('/')}/{key}";

        public async Task<StoredContent?> OpenReadAsync(MediaVisibility area, string key, CancellationToken cancellationToken = default)
        {
            EnsureValid(key);
            try
            {
                var response = await _s3.GetObjectAsync(Bucket(area), key, cancellationToken);
                return new StoredContent(response.ResponseStream, response.LastModified?.ToUniversalTime() ?? DateTime.UtcNow);
            }
            catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public void Dispose() => _s3.Dispose();

        private string Bucket(MediaVisibility area) => area == MediaVisibility.Public ? _options.PublicBucket : _options.PrivateBucket;

        private static void EnsureValid(string key)
        {
            if (!MediaKeys.IsValid(key))
                throw new ArgumentException("Invalid storage key.", nameof(key));
        }
    }
}
