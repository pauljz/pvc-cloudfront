using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Amazon.CloudFront;
using Amazon.CloudFront.Model;
using PvcCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;

namespace PvcPlugins
{
    public class PvcCloudFront : PvcPlugin
    {

        public static string AccessKey;
        public static string SecretKey;
        public static string BucketName;
        public static string DistributionId;
        public static RegionEndpoint RegionEndpoint = RegionEndpoint.USEast1;

        private IAmazonS3 s3client;
        private IAmazonCloudFront cfclient;
        private string accessKey;
        private string secretKey;
        private string bucketName;
        private string distributionId;
        private RegionEndpoint regionEndpoint;
        private string maxAge;
        private string htmlMaxAge;

        private Dictionary<string, string> keyEtags;
        private Dictionary<string, string> keyMD5Sums;

        public PvcCloudFront(
            string accessKey = null,
            string secretKey = null,
            string bucketName = null,
            string distributionId = null,
            Amazon.RegionEndpoint regionEndpoint = null,
            int maxAge = 300,
            int htmlMaxAge = 60)
        {
            this.accessKey = accessKey != null ? accessKey : PvcCloudFront.AccessKey;
            this.secretKey = secretKey != null ? secretKey : PvcCloudFront.SecretKey;
            this.bucketName = bucketName != null ? bucketName : PvcCloudFront.BucketName;
            this.distributionId = distributionId != null ? distributionId : PvcCloudFront.DistributionId;
            this.regionEndpoint = regionEndpoint != null ? regionEndpoint : PvcCloudFront.RegionEndpoint;
            this.maxAge = maxAge.ToString();
            this.htmlMaxAge = htmlMaxAge.ToString();

            // Set up the API client for S3.
            AWSCredentials creds = new BasicAWSCredentials(this.accessKey, this.secretKey);
            this.s3client = new AmazonS3Client(creds, this.regionEndpoint);
            
            // Set up the API client for CloudFront too.
            var cfg = new AmazonCloudFrontConfig() { RegionEndpoint = this.regionEndpoint };
            this.cfclient = new AmazonCloudFrontClient(creds, cfg);

            // Verify that the CloudFront distribution exists. This will throw an Exception
            // if the distribution is inaccessible/doesn't exist.
            var getDistReq = new GetDistributionRequest() { Id = this.distributionId };
            this.cfclient.GetDistribution(getDistReq);

            // Initialize some private stuff that we use to track md5 sums
            this.keyEtags = new Dictionary<string, string>();
            this.keyMD5Sums = new Dictionary<string, string>();
        }

        private string GetHttpDate()
        {
            return System.DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss ", System.Globalization.CultureInfo.InvariantCulture) + "GMT";
        }
        
        private string StreamNameToKey(string streamName)
        {
            return streamName.Replace('\\', '/');
        }

        private string ToHex(byte[] bytes, bool upperCase)
        {
            StringBuilder result = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                result.Append(bytes[i].ToString(upperCase ? "X2" : "x2"));
            return result.ToString();
        }

        private IEnumerable<PvcStream> FilterUploadedFiles(IEnumerable<PvcStream> inputStreams)
        {
            var filteredInputStreams = new List<PvcStream>();

            Console.WriteLine("Checking files in bucket {0}", this.bucketName);
            ListObjectsRequest request = new ListObjectsRequest();
            request.BucketName = this.bucketName;
            var response = this.s3client.ListObjects(request);
            foreach (var o in response.S3Objects)
            {
                keyEtags.Add(o.Key, o.ETag.Trim('"'));
            }

            foreach (var stream in inputStreams)
            {
                using (var md5 = MD5.Create())
                {
                    var md5bytes = md5.ComputeHash(stream);
                    var md5sum = ToHex(md5bytes, false);
                    var md5base64 = Convert.ToBase64String(md5bytes);
                    var key = StreamNameToKey(stream.StreamName);
                    if (!keyEtags.ContainsKey(key) || keyEtags[key] != md5sum)
                    {
                        Console.WriteLine("Including {0} {1}", key, md5sum);
                        filteredInputStreams.Add(stream);
                        keyEtags[key] = md5sum;
                        keyMD5Sums[key] = md5base64;
                    }
                    else
                    {
                        Console.WriteLine("Unchanged {0}", stream.StreamName);
                    }
                }
                stream.ResetStreamPosition();
            }
            return filteredInputStreams;
        }

        public override IEnumerable<PvcStream> Execute(IEnumerable<PvcStream> inputStreams)
        {

            var filteredInputStreams = FilterUploadedFiles(inputStreams);

            var transfer = new TransferUtility(this.s3client);
            var invalidationReq = new CreateInvalidationRequest();
            invalidationReq.DistributionId = this.distributionId;
            invalidationReq.InvalidationBatch = new InvalidationBatch();
            invalidationReq.InvalidationBatch.Paths = new Paths();
            invalidationReq.InvalidationBatch.CallerReference = GetHttpDate();

            foreach (var inputStream in filteredInputStreams)
            {
                if (inputStream.StreamName == null || inputStream.StreamName.Length == 0)
                    continue;

                var uploadReq = new TransferUtilityUploadRequest();
                uploadReq.BucketName = this.bucketName;
                uploadReq.InputStream = inputStream;
                uploadReq.Key = this.StreamNameToKey(inputStream.StreamName);
                uploadReq.Headers.ContentMD5 = this.keyMD5Sums[uploadReq.Key];

                invalidationReq.InvalidationBatch.Paths.Items.Add("/" + uploadReq.Key);

                if (inputStream.Tags.Contains(".html"))
                {
                    uploadReq.Headers.CacheControl = "max-age=" + this.htmlMaxAge;
                    uploadReq.Headers.ContentType = "text/html";
                }
                else
                {
                    uploadReq.Headers.CacheControl = "max-age=" + this.maxAge;
                }

                transfer.Upload(uploadReq);
            };

            if (invalidationReq.InvalidationBatch.Paths.Items.Count > 0)
            {
                invalidationReq.InvalidationBatch.Paths.Quantity = invalidationReq.InvalidationBatch.Paths.Items.Count;
                try
                {
                    var res = this.cfclient.CreateInvalidation(invalidationReq);
                    Console.WriteLine("Invalidation {0} {1}", res.Invalidation.Id, res.Invalidation.Status);
                }
                catch (TooManyInvalidationsInProgressException)
                {
                    Console.WriteLine("CloudFront Invalidation Failed: Too many invalidations in progress.");
                }
                catch (AmazonCloudFrontException e)
                {
                    Console.WriteLine("CloudFront Invalidation Failed: {0}", e.ToString());
                }
            }
            else
            {
                Console.WriteLine("No files changed. Skipping CloudFront invalidation.");
            }

            return inputStreams;
        }
    }
}
