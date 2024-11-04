#!../bin/Release/#S.exe -@
using System;
class avg
{
	public static int Main(string[] args)
	{
		if (args.Length == 0)
			return 0;
		double x = 0;
		for (long i = 0; i < args.Length; i++)
		{
			try {
				x += double.Parse(args[i]);
			} catch {}
		}
		Console.WriteLine(x /= args.Length);
		return (int)x;
	}
}
