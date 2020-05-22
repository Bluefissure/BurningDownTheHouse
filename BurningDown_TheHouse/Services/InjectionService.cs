﻿using ConceptMatrix;
using ConceptMatrix.Injection;
using ConceptMatrix.Injection.Memory;
using ConceptMatrix.Injection.Offsets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace BurningDownTheHouse.Services
{
	public class InjectionService : IInjectionService
	{
		private readonly Dictionary<Type, Type> memoryTypeLookup = new Dictionary<Type, Type>();
		private bool isActive;

		public static InjectionService Instance
		{
			get;
			private set;
		}

		public bool ProcessIsAlive
		{
			get;
			private set;
		}

		public IProcess Process
		{
			get;
			private set;
		}

		public string GamePath
		{
			get
			{
				return Path.GetDirectoryName(this.Process.ExecutablePath) + "\\..\\";
			}
		}

		public Task Initialize()
		{
			Instance = this;
			this.isActive = true;
			this.memoryTypeLookup.Clear();

			// Gets all Memory types (Like IntMemory, FloatMemory) and puts them in the lookup
			foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				foreach (Type type in asm.GetTypes())
				{
					if (type.IsAbstract || type.IsInterface)
						continue;

					if (typeof(MemoryBase).IsAssignableFrom(type))
					{
						if (type.BaseType.IsGenericType)
						{
							Type[] generics = type.BaseType.GetGenericArguments();
							if (generics.Length == 1)
							{
								this.memoryTypeLookup.Add(generics[0], type);
							}
						}
					}
				}
			}

#if NO_GAME
			this.Process = new DummyProcess();
#else
			this.Process = new WinProcess();
#endif

			while (!this.ProcessIsAlive)
			{
				try
				{
					Process[] processes = System.Diagnostics.Process.GetProcesses();
					Process proc = null;
					foreach (Process process in processes)
					{
						if (process.ProcessName.ToLower().Contains("ffxiv_dx11"))
						{
							if (proc != null)
								throw new Exception("Multiple processes found");

							proc = process;
						}
					}

					if (proc == null)
						throw new Exception("No process found");

					this.Process.OpenProcess(proc);
					this.ProcessIsAlive = true;
				}
				catch (Exception ex)
				{
					Log.Write(ex);
				}
			}

			return Task.CompletedTask;
		}

		public Task Start()
		{
			new Thread(new ThreadStart(this.TickMemoryThread)).Start();
			new Thread(new ThreadStart(this.ProcessWatcherThread)).Start();
			return Task.CompletedTask;
		}

		public Task Shutdown()
		{
			this.isActive = false;
			return Task.CompletedTask;
		}

		public IMemory<T> GetMemory<T>(IBaseMemoryOffset baseOffset, params IMemoryOffset[] offsets)
		{
			List<IMemoryOffset> newOffsets = new List<IMemoryOffset>();
			newOffsets.Add(new MappedBaseOffset(this.Process, (BaseOffset)baseOffset));
			newOffsets.AddRange(offsets);
			return this.GetMemory<T>(newOffsets.ToArray());
		}

		public IMemory<T> GetMemory<T>(params IMemoryOffset[] offsets)
		{
			UIntPtr address = this.GetAddress(offsets);

			string offsetString = string.Empty;
			foreach (IMemoryOffset offset in offsets)
			{
				offsetString += " " + GetString(offset) + ",";
			}

			offsetString = offsetString.Trim(' ', ',');

			Type wrapperType = this.GetMemoryType(typeof(T));
			try
			{
				MemoryBase<T> memory = (MemoryBase<T>)Activator.CreateInstance(wrapperType, this.Process, address);
				memory.Description = offsetString + " (" + address + ")";
				return memory;
			}
			catch (TargetInvocationException ex)
			{
				throw ex.InnerException;
			}
		}

		public UIntPtr GetAddress(IBaseMemoryOffset offset)
		{
			IMemoryOffset newOffset = new MappedBaseOffset(this.Process, (BaseOffset)offset);
			return this.Process.GetAddress(newOffset);
		}

		public UIntPtr GetAddress(params IMemoryOffset[] offsets)
		{
			return this.Process.GetAddress(offsets);
		}

		private static string GetString(IMemoryOffset offset)
		{
			Type type = offset.GetType();
			string typeName = type.Name;

			if (type.IsGenericType)
			{
				typeName = typeName.Split('`')[0];
				typeName += "<";

				Type[] generics = type.GetGenericArguments();
				for (int i = 0; i < generics.Length; i++)
				{
					if (i > 1)
						typeName += ", ";

					typeName += generics[i].Name;
				}

				typeName += ">";
			}

			string val = string.Empty;
			for (int i = 0; i < offset.Offsets.Length; i++)
			{
				if (i > 1)
					val += ", ";

				val += offset.Offsets[i].ToString("X2");
			}

			return typeName + " [" + val + "]";
		}

		private Type GetMemoryType(Type type)
		{
			if (!this.memoryTypeLookup.ContainsKey(type))
				throw new Exception($"No memory wrapper for type: {type}");

			return this.memoryTypeLookup[type];
		}

		private void TickMemoryThread()
		{
			try
			{
				while (this.isActive)
				{
					Thread.Sleep(16);

					if (!this.ProcessIsAlive)
						return;

					MemoryBase.TickAllActiveMemory();
				}
			}
			catch (Exception ex)
			{
				Log.Write(new Exception("Memory thread exception", ex));
			}
		}

		private void ProcessWatcherThread()
		{
			while (this.isActive)
			{
				this.ProcessIsAlive = this.Process.IsAlive;

				if (!this.ProcessIsAlive)
				{
					Log.Write(new Exception("FFXIV Process has terminated"), "Injection");
				}

				Thread.Sleep(100);
			}
		}

		public class MappedBaseOffset : IBaseMemoryOffset
		{
			public MappedBaseOffset(IProcess process, BaseOffset offset)
			{
				if (offset.Offsets == null || offset.Offsets.Length <= 0)
					throw new Exception("Invalid base offset");

				this.Offsets = new[] { process.GetBaseAddress() + offset.Offsets[0] };
			}

			public ulong[] Offsets
			{
				get;
				private set;
			}

			public IMemory<T> GetMemory<T>(IMemoryOffset<T> offset)
			{
				throw new NotImplementedException();
			}

			public T GetValue<T>(IMemoryOffset<T> offset)
			{
				throw new NotImplementedException();
			}
		}
	}
}