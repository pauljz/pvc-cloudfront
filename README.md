pvc-cloudfront
========

Uploads files to S3 with cache control headers and invalidates the cache in CloudFront.

Only uploads and invalidates files that have changed.

Example Usage
-------------

```
pvc.Task("cloudfront", () => {
    pvc.Source("test/*")
       .Pipe(new PvcCloudFront(
            accessKey: "ABCDEFABCDEF",
            secretKey: "ABCDEF/abcd/1234",
            bucketName: "pvcs3test",
            distributionId: "E34JMZ2X11F0N7"
       ));
});
```

Gzipped Files
-------------

If the CloudFront plugin sees files that have been tagged with "gzip" (e.g. from the [pvc-gzip](https://github.com/pauljz/pvc-gzip) plugin), it will attach a Content-Encoding: gzip header automatically. Make sure to use `new PvcGzip(addExtension: false)`
