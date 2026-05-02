#nullable disable
/*
 * Copyright (C) 2013 The Libphonenumber Authors
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PhoneNumbers
{
    /// <summary>
    /// A phone prefix mapper which provides carrier information related to a phone number.
    /// <para>
    /// Carrier data is the one the number was originally allocated to. If the country supports mobile
    /// number portability the number might not belong to the returned carrier anymore.
    /// </para>
    /// </summary>
    public class PhoneNumberToCarrierMapper
    {
        // Corresponds to resources/carrier/ embedded with LinkBase="carrier".
        // Resource names become: PhoneNumbers.carrier.{lang}.{cc}.txt
        // MappingFileProvider.GetFileName returns the same "{lang}.{cc}.txt" format.
        private const string MAPPING_DATA_DIRECTORY = "carrier.";

        private static PhoneNumberToCarrierMapper instance;
        private static readonly object ThisLock = new object();

        private readonly PhoneNumberUtil phoneUtil = PhoneNumberUtil.GetInstance();
        private readonly MappingFileProvider mappingFileProvider;
        private readonly Dictionary<string, AreaCodeMap> availablePhonePrefixMaps =
            new Dictionary<string, AreaCodeMap>();
        private readonly string phonePrefixDataDirectory;
        private readonly string phoneDataZipFile;
        private readonly Assembly assembly;

        // @VisibleForTesting
        internal PhoneNumberToCarrierMapper(string phonePrefixDataDirectory, Assembly asm = null)
        {
            SortedDictionary<int, HashSet<string>> files;
            asm ??= typeof(PhoneNumberToCarrierMapper).Assembly;
            var prefix = asm.GetName().Name + "." + phonePrefixDataDirectory;

            var zipFile = prefix + "zip";
            var zipStream = asm.GetManifestResourceStream(zipFile);

            if (zipStream != null)
            {
                using (zipStream)
                {
                    files = LoadFileNamesFromZip(zipStream);
                }
                phoneDataZipFile = zipFile;
            }
            else
            {
                files = LoadFileNamesFromManifestResources(asm, prefix);
            }

            mappingFileProvider = new MappingFileProvider();
            mappingFileProvider.ReadFileConfigs(files);
            assembly = asm;
            this.phonePrefixDataDirectory = prefix;
        }

        // For zipped carrier data: entries follow the same pattern as geocoding — "lang/cc.txt".
        private static SortedDictionary<int, HashSet<string>> LoadFileNamesFromZip(Stream zipStream)
        {
            var files = new SortedDictionary<int, HashSet<string>>();
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                    continue;

                // FullName is like "en/44.txt" — split on path separator, then strip ".txt"
                var pathParts = entry.FullName.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (pathParts.Length < 2)
                    continue;

                var language = pathParts[0];
                var ccPart = pathParts[pathParts.Length - 1].Split('.')[0];
                if (!int.TryParse(ccPart, out var country))
                    continue;

                if (!files.TryGetValue(country, out var languages))
                    files[country] = languages = new HashSet<string>();
                languages.Add(language);
            }
            return files;
        }

        // For unzipped carrier data: resources are "PhoneNumbers.carrier.{lang}.{cc}.txt".
        // After stripping the prefix "PhoneNumbers.carrier." we have: "{lang}.{cc}.txt".
        // Note: languages with underscores (e.g. zh_Hant) appear as a single dotted segment
        // because the file is named "zh_Hant.852.txt" — MSBuild preserves underscores.
        private static SortedDictionary<int, HashSet<string>> LoadFileNamesFromManifestResources(
            Assembly asm, string prefix)
        {
            var files = new SortedDictionary<int, HashSet<string>>();
            var allNames = asm.GetManifestResourceNames();
            var names = allNames.Where(n => n.StartsWith(prefix, StringComparison.Ordinal));
            foreach (var n in names)
            {
                // filePart e.g. "en.44.txt" or "zh_Hant.852.txt"
                var filePart = n.Substring(prefix.Length);
                var parts = filePart.Split('.');
                // Minimum: [lang, cc, "txt"] => length 3
                if (parts.Length < 3)
                    continue;

                // Last segment is "txt", second-to-last is the country calling code.
                // Everything before is the language (joined with '_' to reconstruct "zh_Hant" etc.)
                var ccIdx = parts.Length - 2;
                if (!int.TryParse(parts[ccIdx], out var country))
                    continue;

                var lang = string.Join("_", parts.Take(ccIdx));
                if (lang.Length == 0)
                    continue;

                if (!files.TryGetValue(country, out var languages))
                    files[country] = languages = new HashSet<string>();
                languages.Add(lang);
            }
            return files;
        }

        /// <summary>
        /// Gets the singleton <see cref="PhoneNumberToCarrierMapper"/> instance.
        /// </summary>
        /// <returns>A <see cref="PhoneNumberToCarrierMapper"/> instance.</returns>
        public static PhoneNumberToCarrierMapper GetInstance()
        {
            lock (ThisLock)
            {
                return instance ?? (instance = new PhoneNumberToCarrierMapper(MAPPING_DATA_DIRECTORY));
            }
        }

        /// <summary>
        /// Returns a carrier name for the given phone number, in the language provided.
        /// <para>
        /// The carrier name is the one the number was originally allocated to. If the country supports
        /// mobile number portability the number might not belong to the returned carrier anymore.
        /// If no mapping is found an empty string is returned.
        /// </para>
        /// <para>
        /// This method assumes the validity of the number passed in has already been checked, and that
        /// the number is suitable for carrier lookup. We consider mobile and pager numbers possible
        /// candidates for carrier lookup.
        /// </para>
        /// </summary>
        /// <param name="number">A valid phone number for which we want to get a carrier name.</param>
        /// <param name="languageCode">The language code in which the name should be written.</param>
        /// <returns>A carrier name for the given phone number, or empty string if not found.</returns>
        public string GetNameForValidNumber(PhoneNumber number, Locale languageCode)
        {
            var langStr = languageCode.Language;
            var scriptStr = ""; // No script is specified
            var regionStr = languageCode.Country;
            return GetDescriptionForNumber(number, langStr, scriptStr, regionStr);
        }

        /// <summary>
        /// Gets the name of the carrier for the given phone number, in the language provided.
        /// As per <see cref="GetNameForValidNumber"/> but explicitly checks the validity of
        /// the number passed in, and returns empty string if the number is not a mobile or pager number.
        /// </summary>
        /// <param name="number">The phone number for which we want to get a carrier name.</param>
        /// <param name="languageCode">The language code in which the name should be written.</param>
        /// <returns>A carrier name for the given phone number, or empty string if the number passed
        /// in is invalid or is not a mobile/pager number.</returns>
        public string GetNameForNumber(PhoneNumber number, Locale languageCode)
        {
            var numberType = phoneUtil.GetNumberType(number);
            if (IsMobile(numberType))
                return GetNameForValidNumber(number, languageCode);
            return "";
        }

        /// <summary>
        /// Gets the name of the carrier for the given phone number only when it is 'safe' to display
        /// to users. A carrier name is considered safe if the number is valid and for a region that
        /// doesn't support mobile number portability.
        /// </summary>
        /// <param name="number">The phone number for which we want to get a carrier name.</param>
        /// <param name="languageCode">The language code in which the name should be written.</param>
        /// <returns>A carrier name that is safe to display to users, or the empty string.</returns>
        public string GetSafeDisplayName(PhoneNumber number, Locale languageCode)
        {
            if (phoneUtil.IsMobileNumberPortableRegion(phoneUtil.GetRegionCodeForNumber(number)))
                return "";
            return GetNameForNumber(number, languageCode);
        }

        /// <summary>
        /// Checks if the supplied number type supports carrier lookup.
        /// </summary>
        private static bool IsMobile(PhoneNumberType numberType)
        {
            return numberType == PhoneNumberType.MOBILE
                || numberType == PhoneNumberType.FIXED_LINE_OR_MOBILE
                || numberType == PhoneNumberType.PAGER;
        }

        private string GetDescriptionForNumber(PhoneNumber number, string lang, string script, string region)
        {
            var countryCallingCode = number.CountryCode;
            var phonePrefixDescriptions = GetPhonePrefixDescriptions(countryCallingCode, lang, script, region);
            var description = phonePrefixDescriptions?.Lookup(number);
            // When a carrier name is not available in the requested language, fall back to English.
            if (string.IsNullOrEmpty(description) && MayFallBackToEnglish(lang))
            {
                var defaultMap = GetPhonePrefixDescriptions(countryCallingCode, "en", "", "");
                if (defaultMap == null)
                    return "";
                description = defaultMap.Lookup(number);
            }
            return description ?? "";
        }

        private static bool MayFallBackToEnglish(string lang)
        {
            return !lang.Equals("zh") && !lang.Equals("ja") && !lang.Equals("ko");
        }

        private AreaCodeMap GetPhonePrefixDescriptions(int prefixMapKey, string language, string script, string region)
        {
            var fileName = mappingFileProvider.GetFileName(prefixMapKey, language, script, region);
            if (fileName.Length == 0)
                return null;

            lock (availablePhonePrefixMaps)
            {
                if (!availablePhonePrefixMaps.TryGetValue(fileName, out var map))
                    map = LoadAreaCodeMapFromFile(fileName);
                return map;
            }
        }

        private AreaCodeMap LoadAreaCodeMapFromFile(string fileName)
        {
            var fp = phoneDataZipFile != null
                ? GetManifestZipFileStream(assembly, phoneDataZipFile, fileName)
                : GetManifestFileStream(assembly, phonePrefixDataDirectory, fileName);

            using (fp)
            {
                var areaCodeMap = AreaCodeParser.ParseAreaCodeMap(fp);
                return availablePhonePrefixMaps[fileName] = areaCodeMap;
            }
        }

        // fileName from MappingFileProvider is e.g. "en.44.txt" — direct match to resource suffix.
        private static Stream GetManifestFileStream(Assembly asm, string phonePrefixDataDirectory, string fileName)
        {
            var resName = phonePrefixDataDirectory + fileName;
            return asm.GetManifestResourceStream(resName);
        }

        private static Stream GetManifestZipFileStream(Assembly asm, string phoneDataZipFile, string fileName)
        {
            using var zipStream = asm.GetManifestResourceStream(phoneDataZipFile);
            if (zipStream == null)
                throw new InvalidOperationException("Manifest zip file stream was null.");

            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            var entry = archive.Entries.First(p => Regex.Replace(p.FullName, @"[\\\/]", ".") == fileName);
            using var entryStream = entry.Open();
            var fileStream = new MemoryStream();
            entryStream.CopyTo(fileStream);
            fileStream.Position = 0;
            return fileStream;
        }
    }
}
