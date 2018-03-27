# async-await-sandbox

Async await - .Net helpers for easy async methods

Before async await – each web/api request took a thread from the threadpool, suspending it when not needed

Needs large threadpools, and DoS can be by exhausting threadpool before cpu/disk/network is overwhelmed.

Async await only has a thread when processing, when it's suspended for async, it releases the thread back to the threadpool.

So when it is waiting for async to return, there is NO thread associated with it.

What it does instead is the waiting code knows it has to ask a helper for a thread.  This helper is the Synchronisation Context.  Like a thread context it holds data across the runs, but it is also responsible for getting a thread from the thread pool and returning it.

When I explain things a like to use 2 things – an analogy, and explain what something is NOT.

So I like to think of the Synchronisation Context as our code's butler in the world of async.  It remembers everything we've told it and deals with the admin of handling threads.

So async/await is a simple way to write multithreaded or parallelised code, right?

Erm... NO! :-)

Async/await allos us to handle asynchronous operations, but it does nothing for multi-threaded code or parallelisation.

Because it handles async in a resource-friendly way in terms of threads, it lets other frameworks call our code in a scalable multi-threaded way, but our code is not multithreaded.  In fact in the case of ASP.NET it's quite the opposite and that's one key cause of the async deadlock problem as we'll see shortly.

When a web api request comes in, it uses an ASP.NET Synchronisation context (aka butler) to grab a thread from the threadpool.  It sets up the context with details about the request (http context etc).  It then uses the thread to run the method set up for that route.

If the method is marked async, it will return a task. This amounts to a deferred result, or a "promise" of a result at a later time.  If there is no actual asynchronous code, the result will be ready immediately on return.

If there is an asynchronous call to a website/api/db/disk  what will happen is this.

The webapi request will come in, and the ASP.NET Synchornisation Context will grab a thread from the threadpool, set itself as the Synchronisation Context on that thread and start to run the method.  The method will get to the async code.  When we await the result, it will set things up so that when the async call completes, it will contact the Synchronisation Context, to ask it to get a thread to continue executing.

Having set that up, it will return the task to ASP.NET promising the result.  ASP.NET will then thank the butler and ask them to return the thread to the threadpool (first disassociating itself as the Synchronisation context) and will await the result on completion of the async code.

When the async code completes, it will contact the butler and ask them to get a thread from the threadpool which they will do and the rest of the method will run and the result will be returned.

(or we might rinse and repeat if there's multiple async calls).

So that all works fine.

But what if we use a blocking call – asking for the result of an async call before continuing?

Here's how it goes:

ASP.NET gets the request and asks the ASP.NET Synchronisation Context to get a thread with which it runs the method. When the method gets to the async call, as before , it will set things up so that when the async call completes, it will contact the Synchronisation Context which has been set on the current thread, to ask it to get a thread to continue executing.

But then we make the blocking request for the result.  Whereas before we returned the task to ASP.NET which then released it's thread, we don't do that here, in the method call we wait for the async call to complete.  When it completes it does what we expect and asks the ASP.NET Synchronisation Context to get a thread to continue execution.

BUT ... the ASP.NET Synchornisation Context is a prissy old butler and says "I'm so sorry sir but I'm already handling this thread for ASP.NET, I couldn't possibly divert my attention from that task to deal with another thread ... Yes ASP.NET sir, I'm still awaiting the result of the method, I'll let you know just as soon as it's done... DEADLOCK!

So how to prevent this?  We know that it's common practice to use .ConfigureAwait(false) on an async method call, and this can actually prevent the deadlock.  But what does it do?

Well when we make this async call, the "false" value is for the "preserveContext" parameter.  What that means is that instead of the code resuming by using the prissy ASP.NET Synchronisation Context butler, we use a default Threading Synchronisation Context.  That has no qualms about dealing with multiple threads from the threadpool and so won't deadlock.  It's more like a spiv than a butler – "you wanna thread from the threadpool? I can get you one of 'em, already got one for my old mate ASP.NET..."

It does mean you won't get the context data like the HTTP context, but that's likely not very important.

Trouble is we haven't got rid of ASP.Net butler, he's still there and will be the one called back after  any await that doesn't have .ConfigureAwait(false).  So if any blocking is introduced and .ConfigureAwait(false) isn't used on any single call, even deep in a third party library in the call stack, then we get a deadlock.

Here's two ways to deal with that.
•	New thread
•	Replace current thread sync context
