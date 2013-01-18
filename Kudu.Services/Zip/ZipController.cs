using System;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using Ionic.Zip;
using Kudu.Core;
using Kudu.Core.Settings;
using Kudu.Services.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Kudu.Services.Deployment
{
    public class ZipController : ApiController
    {
        private static object _lockObj = new object();
        private readonly IEnvironment _environment;

        public ZipController(IEnvironment environment, IFileSystem fileSystem)
        {
            _environment = environment;
        }

        /// <summary>
        /// Get all the diagnostic logs as a zip file
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public HttpResponseMessage GetLog()
        {
            lock (_lockObj)
            {
                HttpResponseMessage response = Request.CreateResponse();
                using (var zip = new ZipFile())
                {
                    string path = _environment.RootPath;
                    zip.AddDirectory(path, Path.GetFileName(path));

                    var ms = new MemoryStream();
                    zip.Save(ms);
                    response.Content = ms.AsContent();
                }

                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment");
                response.Content.Headers.ContentDisposition.FileName = String.Format("dump-{0:MM-dd-H:mm:ss}.zip", DateTime.UtcNow);
                return response;
            }
        }
    }
}
