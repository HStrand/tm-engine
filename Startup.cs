using System;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using tm_engine.Serialization;
using tm_engine.Storage;

[assembly: FunctionsStartup(typeof(tm_engine.Startup))]

namespace tm_engine;

public class Startup : FunctionsStartup
{
    public override void Configure(IFunctionsHostBuilder builder)
    {
        builder.Services.AddSingleton(_ => SerializationSettings.Create());
        builder.Services.AddSingleton(_ =>
            new BlobServiceClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage")));
        builder.Services.AddSingleton<IGameStore, BlobGameStore>();
    }
}
