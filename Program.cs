using System;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using System.Collections.Generic;
using System.CodeDom.Compiler;
using System.Reflection;
using Microsoft.CSharp;

static class _______shScr_______
{
	static int Main(string[] args)
	{
		TextWriter OUT = Console.Out, ERR = Console.Error;
		TextReader IN = Console.In;
		
		if (args.Length < 1)
		{
			string exe = Application.ExecutablePath;
			OUT.WriteLine(
				"\n#Script - Execute class-based application sources at runtime\n\n" +
				"Usage: " + Path.GetFileName(exe) + " [options] [source file(s)]\n\nSource options:" +
				"\n  -            Load source file from stdin (use cat when interactive)" +
				"\n  --           Stop parsing for options" +
				"\n  -q           Turn off loading warnings" +
				"\nExecution options:" +
				"\n  -@ ...       Start arguments for the Main method," +
				"\n               if the method's parameters accept string[]" +
				"\n               Can also be placed before the source file" +
				"\n               for compatibility with shebang, but means" +
				"\n               that only one file can be loaded" +
				"\nCompiler options:" +
				"\n  -r DLLPATH   Add reference DLL. Multiple uses acceptable." +
				"\n  -v SDKVER    Set .NET Framework version. Default: " +
#if NET40 // NET20 NET40 NOT AUTOMATICALLY TAKING EFFECT
				"v4.0"
#else
				"v2.0"
#endif
				+ "\n               Supported values: v2.0, v3.5" +
#if NET40
				", v4.0" +
#endif
				"\n  -e CLASS     Select entry point class."
			);
			return 1;
		}

		string[] availableRuntimes = {
			"v2.0",
			"v3.5",
#if NET40
			"v4.0"
#endif
		};
		string defaultRuntime = availableRuntimes[
#if !NET40
			0
#else
			2
#endif
		];
		const string errprefix = "ERROR: ";
		var srcs = new List<string>();
		var refs = new List<string>();
		var conf = new Dictionary<char, object>() {
			{ 'v', "v2.0" },
			{ 'e', null }
		};
		bool noMoreOpts = false, startExecArgs = false, shebangCompat = false, noWarnings = false;
		List<string> copyArgs = new List<string>();
		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];
			if (startExecArgs)
			{
				copyArgs.Add(arg);
				continue;
			}
			if (arg.Length < 1)
			{
				ERR.WriteLine(errprefix + "Null argument!!");
				continue;
			}
			bool swletter = arg[0] == '-';
			if (!noMoreOpts && swletter && arg.Length != 1)
			{
				goto opt;
			}
			string infile;
			if (swletter && arg.Length == 1)
			{
				infile = IN.ReadToEnd();
			}
			else
			{
				if (!File.Exists(arg))
				{
					ERR.WriteLine(errprefix + "File not found: " + arg);
					continue;
				}
				infile = File.ReadAllText(arg);
			}
			if (infile.StartsWith("#!"))
				infile = infile.Substring(infile.IndexOfAny("\r\n".ToCharArray()));
			srcs.Add(infile);
			if (shebangCompat)
				startExecArgs = true;
			continue;
			opt:
			{
				if (arg.Length != 2)
					goto invarg;
				var swname = arg[1];
				switch (swname)
				{
					case '-': // merge into another dictionary?
						noMoreOpts = true;
						break;
					case 'q':
						noWarnings = true;
						break;
					case '@':
						if (shebangCompat)
							ERR.WriteLine(errprefix + arg + " entered twice or more");
						if (srcs.Count == 0)
							shebangCompat = true;
						else
							startExecArgs = true;
						break;
					default:
						if (++i == args.Length)
							goto noargvalue;
						if (!conf.ContainsKey(swname))
							goto invarg;
						switch (swname)
						{
							case 'r':
								refs.Add(args[i]);
								break;
							default:
								conf[swname] = args[i];
								break;
						}
						break;
				}
				continue;
				noargvalue:
					ERR.WriteLine(errprefix + "Missing value for argument " +arg);
					continue;
				invarg:
					ERR.WriteLine(errprefix + "Invalid argument: " +arg);
					continue;
			}
		}
		if (srcs.Count < 1)
		{
			ERR.WriteLine(errprefix + "Not enough source files");
			return 1;
		}
		if (Array.IndexOf(availableRuntimes, conf['v']) < 0)
		{
			const string fault =
#if !NET40
				"v2.0"
#else
				"v4.0"
#endif
			;
			if (!noWarnings)
				ERR.WriteLine("WARNING: Unknown runtime selected: " + conf['v'] + ", defaulting to " + fault);
			conf['v'] = fault;
			/*ERR.WriteLine(
				"WARNING: Unknown runtime selected: " +
				conf['v'] +
				", defaulting to " + (
					conf['v'] = // totally epic technical one liner ruined
#if !NET40
						"v2.0"
#else
						"v4.0"
#endif
			));*/
		}

		bool specifiedEntry = conf['e'] != null;
		//const string baseAsmName = "#Script_Host";
		var compParams = new CompilerParameters(new string[] { }) // would adding stuff like System.Drawing work here
		{
			//MainClass
			GenerateExecutable = false,
			GenerateInMemory = true,
			IncludeDebugInformation = false,
			//OutputAssembly = baseAsmName
			//CompilerOptions
		};
		var prov = new CSharpCodeProvider(
			new Dictionary<string, string>() {
				{ "CompilerVersion", (string)(conf['v']) }
			}
		);
		var res = prov.CompileAssemblyFromSource(compParams, srcs.ToArray());
		if (res.Errors.HasWarnings)
		{
			ERR.WriteLine("Got warnings");
			for (int i = 0; i < res.Errors.Count; i++)
				if (res.Errors[i].IsWarning)
					ERR.WriteLine(res.Errors[i]);
			ERR.Write("----");
		}
		if (res.Errors.HasErrors)
		{
			ERR.WriteLine("Got errors");
			for (int i = 0; i < res.Errors.Count; i++)
				if (!res.Errors[i].IsWarning)
					ERR.WriteLine(res.Errors[i]);
			return 1;
		}
		var classes = res.CompiledAssembly.GetTypes();
		object[] instances = new object[classes.Length];
		MethodInfo main = null;
		int mainIndex = -1;
		bool invalidClassName = true;
		for (int i = 0; i < classes.Length; i++)
		{
			instances[i] = res.CompiledAssembly.CreateInstance(classes[i].FullName);
			MethodInfo m = instances[i].GetType().GetMethod("Main"); // seems redundant lol BUT WHAT DO I KNOW
			if (m != null)
			{
				if (m.IsStatic)
				{
					if (conf['e'] == null || ((string)conf['e']) == classes[i].FullName)
					{
						invalidClassName = false;
						if (mainIndex == -1)
						{
							main = m;
							mainIndex = i;
						}
						else
						{
							ERR.WriteLine(
								errprefix + "There's more than one Main method defined in the source files.\n"+
								"To select a defined entry point, use -e classname.");
							return 1;
						}
					}
				}
			}
		}
		if (invalidClassName)
		{
			ERR.WriteLine(errprefix + "Class name specified does not exist: "+conf['e']+".");
			return 1;
		}
		if (mainIndex == -1 || main == null)
		{
			ERR.WriteLine(errprefix + "Main method could not be found, unless it is a public static method, which is required.");
			return 1;
		}
		object retval = null;
		object[] finalArgs = new object[0];
		{
			ParameterInfo[] pi = main.GetParameters();
			if (pi.Length == 1)
				if (pi[0].ParameterType == (typeof(string[])))
				{
					finalArgs = new object[] { copyArgs.ToArray() };
				}
		}
		retval = main.Invoke(instances[mainIndex], finalArgs);
		if (Type.GetTypeCode(main.ReturnType) == TypeCode.Int32)
			return (int)retval;
		return 0;
	}
}
