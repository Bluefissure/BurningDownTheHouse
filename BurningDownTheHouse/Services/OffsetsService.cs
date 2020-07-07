using BurningDownTheHouse.Files;
using ConceptMatrix;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace BurningDownTheHouse.Services
{
	public class OffsetsService : IService
	{
		private static readonly string FileName = "bdth_offsets.json";
		//private static readonly string Url = $"https://raw.githubusercontent.com/LeonBlade/BurningDownTheHouse/master/BurningDownTheHouse/{FileName}";
		private static readonly string Url = $"https://raw.githubusercontent.com/Bluefissure/BurningDownTheHouse/cn/BurningDownTheHouse/{FileName}";
		private static readonly string LocalOffsetFile = Path.Combine(Environment.CurrentDirectory, FileName);

		public OffsetFile Offsets { get; private set; } = null;

		public async Task Initialize()
		{
			OffsetFile localOffset = null;
			OffsetFile remoteOffset = null;

			// Get the local offset file.
			try
			{
				if (File.Exists(LocalOffsetFile))
				{
					var offsets = JsonConvert.DeserializeObject<OffsetFile>(File.ReadAllText(LocalOffsetFile));
					if (offsets != null)
						localOffset = offsets;
				}
			}
			catch (Exception ex)
			{
				Log.Write(new Exception("Couldn't load offset file", ex), "OffsetService", Log.Severity.Warning);
			}

			// Fetch latest offsets from online.
			using (var client = new HttpClient())
			{
				try
				{
					var response = await client.GetAsync(Url);
					response.EnsureSuccessStatusCode();
					var body = await response.Content.ReadAsStringAsync();
					remoteOffset = JsonConvert.DeserializeObject<OffsetFile>(body);
				}
				catch (HttpRequestException ex)
				{
					Log.Write(new Exception("Couldn't fetch offsets from online!", ex), "OffsetService", Log.Severity.Warning);
				}
			}

			if (remoteOffset == null && localOffset == null)
				throw new Exception("Wasn't able to resolve any offsets");

			// If the offsets are the same.
			if (remoteOffset?.GameVersion == localOffset?.GameVersion)
			{
				// Check which offset version is greater and use it.
				if (remoteOffset?.OffsetVersion > localOffset?.OffsetVersion)
					Offsets = remoteOffset;
				else
					Offsets = localOffset;
			}
			else
			{
				// Use the offset with the latest game version.
				if (remoteOffset?.GameVersion.CompareTo(localOffset?.GameVersion) > 0)
					Offsets = remoteOffset;
				else
					Offsets = localOffset;
			}

			if (Offsets == null)
				throw new Exception("Wasn't able to resolve any offsets");
		}

		public Task Start()
		{
			return Task.CompletedTask;
		}

		public Task Shutdown()
		{
			return Task.CompletedTask;
		}

		public ulong[] Get(string propName)
		{
			foreach (var prop in Offsets.GetType().GetProperties())
			{
				if (prop.PropertyType == typeof(string) && prop.Name == propName)
				{
					var val = (string)prop.GetValue(Offsets);
					if (val.IndexOf(',') > -1)
					{
						var retArr = new List<ulong>();
						var arr = val.Split(',');
						foreach (var s in arr)
							retArr.Add(Convert.ToUInt64(s, 16));
						return retArr.ToArray();
					}

					return new ulong[] { Convert.ToUInt64(val, 16) };
				}
			}

			throw new Exception($"No property with that name: {propName}");
		}
	}
}
