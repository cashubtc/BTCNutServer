using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Cashu.Services;

public class CashuSwaggerProvider : ISwaggerProvider
{
    private readonly IFileProvider _fileProvider;

    public CashuSwaggerProvider(IWebHostEnvironment webHostEnvironment)
    {
        _fileProvider = webHostEnvironment.WebRootFileProvider;
    }

    public async Task<JObject> Fetch()
    {
        var fi = _fileProvider.GetFileInfo("Resources/swagger/v1/swagger.template.cashu.json");
        await using var stream = fi.CreateReadStream();
        using var reader = new StreamReader(stream);
        return JObject.Parse(await reader.ReadToEndAsync());
    }
}
