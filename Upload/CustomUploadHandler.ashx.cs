﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Script.Serialization;

namespace jQuery_File_Upload.MVC3.Upload
{
    /// <summary>
    /// Summary description for UploadHandler
    /// </summary>
    public class CustomUploadHandler : IHttpHandler
    {
        private readonly JavaScriptSerializer js;

        private string StorageRoot
        {
            //@@EMAGNO - parametrizar StorageRoot (ServicoCore ??)
            get { return Path.Combine(System.Web.HttpContext.Current.Server.MapPath("~/SingleFiles/")); } //Path should! always end with '/'
        }

        private string _storageFolder;
        private string StorageFolder
        {
            get
            {
                return _storageFolder;
            }
            set
            {
                if (value != null)
                {
                    var subFolder = Path.Combine(StorageRoot, value);

                    if (!Directory.Exists(subFolder))
                    {
                        Directory.CreateDirectory(subFolder);
                    }

                    _storageFolder = subFolder + @"\\";
                }
                else
                {
                    _storageFolder = StorageRoot;
                }
            }
        }

        public CustomUploadHandler()
        {
            js = new JavaScriptSerializer();
            js.MaxJsonLength = 41943040;
        }

        public bool IsReusable { get { return false; } }

        public void ProcessRequest(HttpContext context)
        {
            try
            {
                var folder = context.Request["folder"] ?? "";
                var subfolder = context.Request["subfolder"] ?? "";
                StorageFolder = Path.Combine(folder, subfolder);

                context.Response.AddHeader("Pragma", "no-cache");
                context.Response.AddHeader("Cache-Control", "private, no-cache");

                HandleMethod(context);
            }
            catch (System.Exception)
            {
                // @@EMAGNO - tratar exceções como mensagem amigavel

                context.Response.AddHeader("Pragma", "no-cache");
                context.Response.AddHeader("Cache-Control", "private, no-cache");
                context.Response.ClearHeaders();
                context.Response.StatusCode = 405;
            }
        }

        // Handle request based on method
        private void HandleMethod(HttpContext context)
        {
            switch (context.Request.HttpMethod)
            {
                case "HEAD":
                case "GET":
                    if (GivenFilename(context)) DeliverFile(context);
                    else ListCurrentFiles(context);
                    break;

                case "POST":
                case "PUT":
                    UploadFile(context);
                    break;

                case "DELETE":
                    DeleteFile(context);
                    break;

                case "OPTIONS":
                    ReturnOptions(context);
                    break;

                default:
                    context.Response.ClearHeaders();
                    context.Response.StatusCode = 405;
                    break;
            }
        }

        private static void ReturnOptions(HttpContext context)
        {
            context.Response.AddHeader("Allow", "DELETE,GET,HEAD,POST,PUT,OPTIONS");
            context.Response.StatusCode = 200;
        }

        // Delete file from the server
        private void DeleteFile(HttpContext context)
        {
            var st = context.Request["fp"];

            var filePath = st; //st + context.Request["f"];
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        // Upload file to the server
        private void UploadFile(HttpContext context)
        {
            var statuses = new List<CustomFilesStatus>();
            var headers = context.Request.Headers;

            if (string.IsNullOrEmpty(headers["X-File-Name"]))
            {
                UploadWholeFile(context, statuses);
            }
            else
            {
                UploadPartialFile(headers["X-File-Name"], context, statuses);
            }

            WriteJsonIframeSafe(context, statuses);
        }

        // Upload partial file
        private void UploadPartialFile(string fileName, HttpContext context, List<CustomFilesStatus> statuses)
        {
            if (context.Request.Files.Count != 1) throw new HttpRequestValidationException("Attempt to upload chunked file containing more than one fragment per request");
            var inputStream = context.Request.Files[0].InputStream;
            var fullName = StorageFolder + Path.GetFileName(fileName);

            using (var fs = new FileStream(fullName, FileMode.Append, FileAccess.Write))
            {
                var buffer = new byte[1024];

                var l = inputStream.Read(buffer, 0, 1024);
                while (l > 0)
                {
                    fs.Write(buffer, 0, l);
                    l = inputStream.Read(buffer, 0, 1024);
                }
                fs.Flush();
                fs.Close();
            }
            statuses.Add(new CustomFilesStatus(new FileInfo(fullName)));
        }

        // Upload entire file
        private void UploadWholeFile(HttpContext context, List<CustomFilesStatus> statuses)
        {
            for (int i = 0; i < context.Request.Files.Count; i++)
            {
                var file = context.Request.Files[i];

                var fullPath = StorageFolder + Path.GetFileName(file.FileName);

                //@@EMAGNO - normalizar nome do arquivo: remover acentuação e caracteres especiais
                file.SaveAs(fullPath);

                string fullName = Path.GetFileName(file.FileName);
                statuses.Add(new CustomFilesStatus(fullName, file.ContentLength, fullPath));
            }
        }

        private void WriteJsonIframeSafe(HttpContext context, List<CustomFilesStatus> statuses)
        {
            context.Response.AddHeader("Vary", "Accept");
            try
            {
                if (context.Request["HTTP_ACCEPT"].Contains("application/json"))
                    context.Response.ContentType = "application/json";
                else
                    context.Response.ContentType = "text/plain";
            }
            catch
            {
                context.Response.ContentType = "text/plain";
            }

            var jsonObj = js.Serialize(statuses.ToArray());
            context.Response.Write(jsonObj);
        }

        private static bool GivenFilename(HttpContext context)
        {
            return !string.IsNullOrEmpty(context.Request["f"]);
        }

        private void DeliverFile(HttpContext context)
        {
            var filename = context.Request["f"];
            var filePath = context.Request["fp"]; //StorageFolder + filename;

            if (File.Exists(filePath))
            {
                context.Response.AddHeader("Content-Disposition", "attachment; filename=\"" + filename + "\"");
                context.Response.ContentType = "application/octet-stream";
                context.Response.ClearContent();
                context.Response.WriteFile(filePath);
            }
            else
                context.Response.StatusCode = 404;
        }

        private void ListCurrentFiles(HttpContext context)
        {
            StorageFolder = context.Request["folder"];

            if (!Directory.Exists(StorageFolder)) return;

            var files =
                new DirectoryInfo(StorageFolder)
                    .GetFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden))
                    .Select(f => new CustomFilesStatus(f))
                    .ToArray();

            string jsonObj = js.Serialize(files);
            context.Response.AddHeader("Content-Disposition", "inline; filename=\"files.json\"");
            context.Response.Write(jsonObj);
            context.Response.ContentType = "application/json";
        }

    }
}