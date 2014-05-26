pvc-cloudfront
========

Uploads files to S3 with cache control headers and invalidates the cache in CloudFront.

Only uploads and invalidates files that have changed.

Example Usage
-------------

    pvc.Task("testcf", () => {
            pvc.Source("test/*")
               .Pipe(new PvcCloudFront(
                    accessKey: "ABCDEFABCDEF",
                    secretKey: "ABCDEF/abcd/1234",
                    bucketName: "pvcs3test",
                    distributionId: "E34JMZ2X11F0N7"
               ));
    });