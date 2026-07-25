using System;
using System.Collections.Generic;
using System.Reflection;

namespace EM.RosterIntel.Util;

public static class SafeReflection
{
	public static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			List<Type> list = new List<Type>();
			Type[] types = ex.Types;
			foreach (Type type in types)
			{
				if (type != null)
				{
					list.Add(type);
				}
			}
			return list;
		}
		catch
		{
			return Array.Empty<Type>();
		}
	}
}
