pvc.Task("nuget-push", () => {
    pvc.Source("src/Pvc.CloudFront.csproj")
       .Pipe(new PvcNuGetPack(
            createSymbolsPackage: true
       ))
       .Pipe(new PvcNuGetPush());
});
