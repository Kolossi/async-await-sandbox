using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using Newtonsoft.Json.Linq;

namespace AsyncWebApplication.Controllers
{
    /// <summary>
    /// The issue is that the WebApi system itself "locks" the
    /// SynchronisationContext whilst it waits for the call to complete.
    /// So if we make an await blocking by requesting its result, the
    /// completion of the await needs the SynchronisationContext to run -
    /// so we get a deadlock.
    /// 
    /// If the call returns a task, none of the awaits try to resolve until
    /// back in the WebApi lib, which by then has released its "lock", so no
    /// deadlock.
    /// 
    /// If we call .ConfigureAwait(continueOnCapturedContext:false) we are
    /// saying don't complete this call using the SynchronisationContext locked
    /// by WebApi. So our await won't deadlock, but if anyone down the call
    /// chain doesnt do this the deadlock will still occur.
    /// 
    /// More reading:
    /// 
    /// * https://blog.stephencleary.com/2012/07/dont-block-on-async-code.html
    /// * https://blog.stephencleary.com/2012/02/async-and-await.html
    /// * https://msdn.microsoft.com/en-us/magazine/jj991977.aspx
    /// * https://blog.stephencleary.com/2013/11/there-is-no-thread.html
    /// * https://stackoverflow.com/questions/21013751/what-is-the-async-await-equivalent-of-a-threadpool-server/21018042#21018042
    /// * https://msdn.microsoft.com/en-us/magazine/gg598924.aspx
    /// * https://stackoverflow.com/questions/12659851/aspnetsynchronizationcontext
    /// 
    /// </summary>
    public class ValuesController : ApiController
    {
        /// <summary>
        /// result1 and result 2 are independent, we return a task and
        /// it all gets resolved later up in the WebApi caller
        /// so it's non-blocking
        /// </summary>
        [HttpGet]
        [Route("api/values/asyncnonblocking")]
        public async Task<IEnumerable<string>> AsyncNonBlockingOk()
        {
            var result1 = await GetStringAsync();

            var result2 = "bar";

            return new string[] { result1, result2 };
        }

        /// <summary>
        /// result1 and result 2 are independent, we return a task and
        /// it all gets resolved later up in the WebApi caller
        /// so it's non-blocking even without .ConfigureAwait(false)
        /// </summary>
        [HttpGet]
        [Route("api/values/asyncnonblocking_oops")]
        public async Task<IEnumerable<string>> AsyncNonBlockingOopsOk()
        {
            var result1 = await GetStringAsyncOopsForgetConfigureAwaitFalse();

            var result2 = "bar";

            return new string[] { result1, result2 };
        }

        /// <summary>
        /// although result2 depends on result1, since we return a task
        /// it all gets resolved later up in the WebApi caller
        /// so it's non-blocking
        /// </summary>
        [HttpGet]
        [Route("api/values/asyncdependant")]
        public async Task<IEnumerable<string>> AsyncDependantOk()
        {
            var result1 = await GetStringAsync();

            var result2 = result1 + "bar"; 

            return new string[] { result1, result2 };
        }

        /// <summary>
        /// although result2 depends on result1, since we return a task
        /// it all gets resolved later up in the WebApi caller
        /// so it's non-blocking even without .ConfigureAwait(false)
        /// </summary>
        [HttpGet]
        [Route("api/values/asyncdependant_oops")]
        public async Task<IEnumerable<string>> AsyncDependantOopsOk()
        {
            var result1 = await GetStringAsyncOopsForgetConfigureAwaitFalse();

            var result2 = result1 + "bar";

            return new string[] { result1, result2 };
        }

        
        /// <summary>
        /// result2 depends on result1, and we block to get the result1
        /// before calculating result2.  Since we used .ConfigureAwait(false)
        /// we do not need to use the "locked" SynchronisationContext to
        /// complete the result, and everything finishes ok
        /// </summary>
        [HttpGet]
        [Route("api/values/blocking")]
        public IEnumerable<string> BlockingOk()
        {
            var result1 = GetStringAsync().Result; // <-- ".Result" means we block

            var result2 = result1 + "bar";

            return new string[] { result1, result2 };
        }

        /// <summary>
        /// result2 depends on result1, and we block to get the result1
        /// before calculating result2.  Since we didnt .ConfigureAwait(false)
        /// we need the "locked" SynchronisationContext to  complete the result,
        /// and we get a deadlock
        /// </summary>
        [HttpGet]
        [Route("api/values/blocking_oops_DEADLOCK")]
        public IEnumerable<string> BlockingOopsDEADLOCK()
        {
            var result1 = GetStringAsyncOopsForgetConfigureAwaitFalse().Result; // <-- ".Result" means we block

            var result2 = result1 + "bar";

            return new string[] { result1, result2 };
        }

        /// <summary>
        /// Rather than using the WebApi thread and get its "locked"
        /// SynchronisationContext, we'll run this on a new threadpool thread.
        /// This means we use 2 threads rather than one, but we don't use
        /// any more than that for the rest of the awaits down the call chain.
        /// It's non-blocking even without .ConfigureAwait(false)
        /// </summary>
        [HttpGet]
        [Route("api/values/blocking_oops_new_thread")]
        public IEnumerable<string> BlockingOopsFixedNewThread()
        {
            string result1 = null;

            Task.Run(async delegate
            {
                result1 = GetStringAsyncOopsForgetConfigureAwaitFalse().Result.ToString();
            }).Wait(); // <-- block

            var result2 = result1 + "bar";

            return new string[] { result1, result2 };
        }


        [HttpGet]
        [Route("api/values/blocking_oops_clear_context")]
        public IEnumerable<string> BlockingOopsFixedClearContext()
        {
            HttpContext.Current.Items["foobar"] = "wow";
            var before = HttpContext.Current.Items["foobar"].ToString();
            System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Threading.SynchronizationContext());
            var after = HttpContext.Current.Items["foobar"].ToString();
            var result1 = GetStringAsyncOopsForgetConfigureAwaitFalse().Result; // <-- ".Result" means we block

            var result2 = result1 + "bar";

            return new string[] { result1, result2, before, after };
        }

        [HttpGet]
        [Route("api/values/blocking_oops_copy_context_DEADLOCK")]
        public IEnumerable<string> BlockingOopsCopyContextDEADLOCK()
        {
            HttpContext.Current.Items["foobar"] = "wow";
            var scNow = System.Threading.SynchronizationContext.Current;
            var before = HttpContext.Current.Items["foobar"].ToString();
            System.Threading.SynchronizationContext.SetSynchronizationContext(scNow.CreateCopy());
            var after = HttpContext.Current.Items["foobar"].ToString();
            var result1 = GetStringAsyncOopsForgetConfigureAwaitFalse().Result; // <-- ".Result" means we block

            var result2 = result1 + "bar";

            return new string[] { result1, result2, before, after };
        }


        public static async Task<string> GetStringAsyncOopsForgetConfigureAwaitFalse()
        {
            using (var client = new HttpClient())
            {
                var resultString = await client.GetStringAsync(new Uri("http://pp.travelrepublic.co.uk/version.txt"));
                return resultString;
            }
        }

        public static async Task<string> GetStringAsync()
        {
            using (var client = new HttpClient())
            {
                var resultString = await client.GetStringAsync(new Uri("http://pp.travelrepublic.co.uk/version.txt")).ConfigureAwait(false);
                return resultString;
            }
        }


        //%%%  use GetStringWithContext and check results


        //public static async Task<string> GetStringAsyncWithContextOopsForgetConfigureAwaitFalse()
        //{
        //    using (var client = new HttpClient())
        //    {
        //        var resultString = await client.GetStringAsync(new Uri("http://pp.travelrepublic.co.uk/version.txt"));
        //        return resultString + HttpContext.Current.Items["foo"];
        //    }
        //}

        //public static async Task<string> GetStringAsyncWithContext()
        //{
        //    using (var client = new HttpClient())
        //    {
        //        var resultString = await client.GetStringAsync(new Uri("http://pp.travelrepublic.co.uk/version.txt")).ConfigureAwait(false);
        //        return resultString + HttpContext.Current.Items["foo"];
        //    }
        //}
    }
}
