using RGiesecke.DllExport;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Virtalis.SDK.VRTreeNative;
using Microsoft.CognitiveServices.Speech;
using System.Collections.Concurrent;

namespace AzureSpeechToText
{
  /// <summary>
  /// Implementation of the speech plugin discussed in https://developers.virtalis.com/blogs/intuitive-immersive-annotation-using-speech-recognition
  /// </summary>
  public class Exports
  {
    /// <summary>
    /// Store some delegate functions that we are passing to the native API, so they don't get garbage-collected
    /// </summary>
    public static FFI.FFIFunction StartListeningDelegate;
    public static FFI.FFIFunction StopListeningDelegate;
    public static Observer.UpdateFunction UpdateDelegate;

    /// <summary>
    /// The Azure speech recognizer object
    /// </summary>
    public static SpeechRecognizer Recognizer;

    /// <summary>
    /// Item in the message queue, specifying a Lua callback function name, and parameter to pass
    /// </summary>
    public struct QueueItem
    { 
      public string Callback;
      public string Param;
    }
    /// <summary>
    /// Queue of messages from completed speech to text operations, to be submitted to the VisRen Lua state
    /// </summary>
    public static ConcurrentQueue<QueueItem> MessageQueue;

    /// <summary>
    /// Boilerplate to allow VisRen plugins to load dependency DLLs from their own plugin folder
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    /// <returns></returns>
    static Assembly LoadFromSameFolder(object sender, ResolveEventArgs args)
    {
      Assembly assembly = null;
      try
      {
        string folderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string assemblyPath = Path.Combine(folderPath, new AssemblyName(args.Name).Name + ".dll");

        if (!File.Exists(assemblyPath)) return null;
        assembly = Assembly.LoadFrom(assemblyPath);
      }
      catch (Exception e)
      {
        Console.WriteLine(e.Message);
      }

      return assembly;
    }

    static Exports()
    {
      AppDomain currentDomain = AppDomain.CurrentDomain;
      currentDomain.AssemblyResolve += new ResolveEventHandler(LoadFromSameFolder);
    }

    /// <summary>
    /// Required exported function to allow VisRen to detect this plugin
    /// </summary>
    /// <returns></returns>
    [DllExport("VRPGetAPIVersionMajor", CallingConvention = CallingConvention.Cdecl)]
    public static int MajorV()
    {
      return 1;
    }

    /// <summary>
    /// Required exported function to allow VisRen to detect this plugin
    /// </summary>
    /// <returns></returns>
    [DllExport("VRPGetAPIVersionMinor", CallingConvention = CallingConvention.Cdecl)]
    public static int MinorV()
    {
      return 1;
    }

    /// <summary>
    /// Function registered in the Lua state to allow VisRen scripts to activate the speech recognizer
    /// </summary>
    /// <param name="argv"></param>
    /// <param name="ud"></param>
    /// <returns></returns>
    public static FFIVarHandle OnStartListening(FFIVarHandle[] argv, IntPtr ud)
    {
      Console.WriteLine("OnStartListening");
      Recognizer.StartContinuousRecognitionAsync();
      return new FFIVarHandle();
    }

    /// <summary>
    /// Function registered in the Lua state to allow VisRen scripts to deactivate the recognizer
    /// </summary>
    /// <param name="argv"></param>
    /// <param name="ud"></param>
    /// <returns></returns>
    public static FFIVarHandle OnStopListening(FFIVarHandle[] argv, IntPtr ud)
    {
      Console.WriteLine("OnStopListening");
      Recognizer.StopContinuousRecognitionAsync();
      return new FFIVarHandle();
    }

    /// <summary>
    /// Function registered with VisRen to be called once per frame, we use it to process our queue messages
    /// and trigger Lua callbacks when speech recognition is complete.
    /// See https://developers.virtalis.com/blogs/thread-safety-in-visionary-render-plugins
    /// </summary>
    /// <param name="dt"></param>
    /// <param name="ud"></param>
    public static void Update(double dt, IntPtr ud)
    {
      while(MessageQueue.TryDequeue(out var item))
      {
        FFIVarHandle[] args = {
          FFI.MakeString(item.Callback),
          FFI.MakeString(item.Param ?? "")
        };

        FFI.Invoke("__callback", args);
      }
    }

    /// <summary>
    /// Main plugin initialization function called by VisRen after it loads the plugin
    /// </summary>
    /// <returns></returns>
    [DllExport("VRPInit", CallingConvention = CallingConvention.Cdecl)]
    public static int Init()
    {
      // Create a queue to receive completed speech to text results
      MessageQueue = new ConcurrentQueue<QueueItem>();

      // Configure the recognizer. This requires you to have an active Azure Speech to Text service subscription:
      // https://azure.microsoft.com/en-gb/services/cognitive-services/speech-to-text
      var config = SpeechConfig.FromSubscription("YOUR_SUBSCRIPTION_KEY", "YOUR_SUBSCRIPTION_REGION");
      Recognizer = new SpeechRecognizer(config);

      // Add a callback to receive recognized text, and queue it up for a Lua callback called "onSpeech", which should be registered by the caller in Lua
      Recognizer.Recognized += (s, e) =>
      {
        if(e.Result.Reason == ResultReason.RecognizedSpeech)
        {
          Console.WriteLine("Recognized: " + e.Result.Text);
          MessageQueue.Enqueue(new QueueItem { Callback = "onSpeech", Param = e.Result.Text });
        }
      };

      EventHandler<RecognitionEventArgs> MakeEventFunc(string name) => 
        (object s, RecognitionEventArgs e) => MessageQueue.Enqueue(new QueueItem { Callback = name });

      // Register some Lua callbacks for the other events we get from the recognizer
      Recognizer.SessionStarted += (s, e) => MakeEventFunc("onSpeechSessionStarted");
      Recognizer.SessionStopped += (s, e) => MakeEventFunc("onSpeechSessionStopped");
      Recognizer.SpeechStartDetected += (s, e) => MakeEventFunc("onSpeechStartDetected");
      Recognizer.SpeechEndDetected += (s, e) => MakeEventFunc("onSpeechEndDetected");

      // Register our Lua function to enable listening
      StartListeningDelegate = new FFI.FFIFunction(OnStartListening);
      FFI.RegisterGlobalFunction("StartListening", StartListeningDelegate, 0, IntPtr.Zero);

      // Register our Lua function to stop listening
      StopListeningDelegate = new FFI.FFIFunction(OnStopListening);
      FFI.RegisterGlobalFunction("StopListening", StopListeningDelegate, 0, IntPtr.Zero);

      // Register our update function for queue processing
      UpdateDelegate = new Observer.UpdateFunction(Update);
      Observer.AddCallbackUpdate(UpdateDelegate, IntPtr.Zero);

      // zero is init success
      return 0;
    }

    /// <summary>
    /// Function called by VisRen when the plugin is unloaded
    /// </summary>
    /// <returns></returns>
    [DllExport("VRPCleanup", CallingConvention = CallingConvention.Cdecl)]
    public static int Cleanup()
    {
      // Unregister all our callbacks from the native API
      Observer.RemoveCallbackUpdate(UpdateDelegate, null);
      FFI.UnregisterGlobalFunction("StartListening", StartListeningDelegate);
      FFI.UnregisterGlobalFunction("StopListening", StopListeningDelegate);

      // zero is cleanup success
      return 0;
    }

    /// <summary>
    /// Required export for VisRen to know the name of this plugin
    /// </summary>
    /// <returns></returns>
    [DllExport("VRPName", CallingConvention = CallingConvention.Cdecl)]
    public static string Name()
    {
      return "AzureSpeechToText";
    }

    /// <summary>
    /// Required export for VisRen to know the name of this plugin
    /// </summary>
    /// <returns></returns>
    [DllExport("VRPShortName", CallingConvention = CallingConvention.Cdecl)]
    public static string ShortName()
    {
      return "AzureSpeechToText";
    }

    /// <summary>
    /// Required export for VisRen to know the version of this plugin
    /// </summary>
    /// <returns></returns>
    [DllExport("VRPVersion", CallingConvention = CallingConvention.Cdecl)]
    public static string Version()
    {
      return "0.1";
    }

    /// <summary>
    /// Required export for VisRen to enable access to the native API
    /// </summary>
    /// <returns></returns>
    [DllExport("VRPSignature", CallingConvention = CallingConvention.Cdecl)]
    public static string Signature()
    {
      return "YOUR_VISREN_NATIVE_API_KEY";
    }
  }
}
