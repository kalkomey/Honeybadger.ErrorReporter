using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.Security;

namespace Honeybadger.ErrorReporter
{
    public class HoneybadgerService
    {
        public bool ReportException(Exception exception, out string honeybadgerResponse)
        {
            while (exception.InnerException != null)
            {
                exception = exception.InnerException;
            }
            var reportingContent = GetReportingContent(exception);

            var reportingJson = new JavaScriptSerializer().Serialize(reportingContent);

            return SendErrorToHoneybadger(reportingJson, out honeybadgerResponse);
        }

        private static dynamic GetReportingContent(Exception exception)
        {
            var context = HttpContext.Current;
            var server = context.Server;
            return new
            {
                notifier = GetNotifier(),
                error = GetErrorInformation(exception),
                request = GetRequestInformation(context.Request),
                server = new
                {
                    project_root = new
                    {
                        path = server.MapPath("~/")
                    },
                    environment_name = ConfigurationManager.AppSettings["honeybadger-environment-name"],
                    hostname = context.Request.ServerVariables["SERVER_NAME"]
                }
            };
        }

        private static dynamic GetRequestInformation(HttpRequest request)
        {
            Dictionary<string, object> result = new Dictionary<string, object>()
            {
                { "url", request.Url.ToString() },
                { "form", request.Form },
                { "context", GetContext() },
                { "cgi_data", GetCGIData(request) }
            };
            object paramsResult = GetParams(request);
            if (paramsResult != null)
            {
                result["params"] = paramsResult;
            }
            return result;
        }

        private static dynamic GetContext()
        {
            MembershipUser membershipUser = null;
            try
            {
                membershipUser = Membership.GetUser();
            }
            catch { }
            dynamic context = null;
            if (membershipUser != null)
            {
                context = new
                {
                    username = membershipUser.UserName,
                    user_id = membershipUser.ProviderUserKey
                };
            }
            return context;
        }

        private static object GetParams(HttpRequest request)
        {
            string requestBody = null;
            try
            {
                MemoryStream memstream = new MemoryStream();
                request.InputStream.CopyTo(memstream);
                memstream.Position = 0;
                using (StreamReader reader = new StreamReader(memstream))
                {
                    requestBody = reader.ReadToEnd();
                }

                if (!string.IsNullOrEmpty(requestBody))
                {
                    try
                    {
                        if (request.Params != null && request.Params["CONTENT_TYPE"] != null && request.Params["CONTENT_TYPE"].ToLower() == "application/json")
                        {
                            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(requestBody);
                        }
                    }
                    catch { }

                    // return raw body as string
                    return new
                    {
                        request_body = requestBody
                    };
                }
            }
            catch { }

            return null;
        }

        private static List<string> CGIKeys = new List<string>()
        {
            "AUTH_USER",
            "CONTENT_LENGTH",
            "CONTENT_TYPE",
            "GATEWAY_INTERFACE",
            "HTTPS",
            "LOCAL_ADDR",
            "PATH_INFO",
            "QUERY_STRING",
            "REMOTE_ADDR",
            "REMOTE_HOST",
            "REMOTE_PORT",
            "REQUEST_METHOD",
            "SERVER_PROTOCOL",
            "SERVER_SOFTWARE",
            "HTTP_CACHE_CONTROL",
            "HTTP_CONNECTION",
            "HTTP_CONTENT_LENGTH",
            "HTTP_CONTENT_TYPE",
            "HTTP_ACCEPT",
            "HTTP_ACCEPT_ENCODING",
            "HTTP_ACCEPT_LANGUAGE",
            "HTTP_HOST",
            "HTTP_USER_AGENT",
            "HTTP_ORIGIN"
        };

        private static object GetCGIData(HttpRequest request)
        {
            if (request.Params == null)
            {
                return null;
            }

            Dictionary<string, string> paramValues = CGIKeys.ToDictionary(k => k, k => request.Params[k]);
            return paramValues;
        }

        private static dynamic GetErrorInformation(Exception exception)
        {
            return new
            {
                @class = exception.GetType().Name,
                message = exception.Message,
                backtrace = GetBacktraceFromException(exception)
            };
        }

        private static dynamic GetNotifier()
        {
            return new
            {
                name = "Honeybadger .NET Notifier",
                url = "https://github.com/webnuts/honeybadger-error-reporter",
                version = "1.0"
            };
        }

        private static bool SendErrorToHoneybadger(string reportingJson, out string honeybadgerResponse)
        {
            var httpWebRequest = (HttpWebRequest)WebRequest.Create("https://api.honeybadger.io/v1/notices");
            httpWebRequest.ContentType = "application/json";
            httpWebRequest.Accept = "application/json";
            httpWebRequest.Method = "POST";

            httpWebRequest.Headers.Add("X-API-Key", ConfigurationManager.AppSettings["honeybadger-api-key"]);

            using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
            {
                streamWriter.Write(reportingJson);
                streamWriter.Flush();
                streamWriter.Close();

                var webResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                using (var responseStream = webResponse.GetResponseStream())
                {
                    honeybadgerResponse = responseStream != null ? new StreamReader(responseStream).ReadToEnd() : "";
                }

                if (webResponse.StatusCode != HttpStatusCode.Created)
                {
                    return false;
                }
            }
            return true;
        }

        private static List<dynamic> GetBacktraceFromException(Exception exception)
        {
            var backtrace = new List<dynamic>();
            var stackTrace = new StackTrace(exception, true);
            for (var i = 0; i < stackTrace.FrameCount; i++)
            {
                var frame = stackTrace.GetFrame(i);
                backtrace.Add(new
                {
                    number = frame.GetFileLineNumber(),
                    file = frame.GetFileName(),
                    method = frame.GetMethod().Name
                });
            }
            return backtrace;
        }
    }
}