using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using SourceSchemaParser;
using AutoMapper;
using SourceSchemaParser.Utilities;

[assembly: FunctionsStartup(typeof(DotaDatabaseFunctions.Startup))]

namespace DotaDatabaseFunctions
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddSourceSchemaParser();
            builder.Services.AddAutoMapper(typeof(SchemaParser));
        }
    }
}
