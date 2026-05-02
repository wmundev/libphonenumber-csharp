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

using Xunit;

namespace PhoneNumbers.Test
{
    [Collection("TestMetadataTestCase")]
    public class TestPhoneNumberToCarrierMapper
    {
        private readonly PhoneNumberToCarrierMapper carrierMapper = PhoneNumberToCarrierMapper.GetInstance();

        // UK mobile: 447106 -> O2, 447306 -> Virgin Mobile (en/44.txt)
        private static readonly PhoneNumber UK_MOBILE1 =
            new PhoneNumber.Builder().SetCountryCode(44).SetNationalNumber(7106123456L).Build();
        private static readonly PhoneNumber UK_MOBILE2 =
            new PhoneNumber.Builder().SetCountryCode(44).SetNationalNumber(7306123456L).Build();
        private static readonly PhoneNumber UK_FIXED1 =
            new PhoneNumber.Builder().SetCountryCode(44).SetNationalNumber(2071234567L).Build();
        // Too short to be valid — UNKNOWN type
        private static readonly PhoneNumber UK_INVALID_NUMBER =
            new PhoneNumber.Builder().SetCountryCode(44).SetNationalNumber(7301234L).Build();

        // Angola mobile: 24491 -> Movicel, 24492 -> UNITEL (en/244.txt)
        private static readonly PhoneNumber AO_MOBILE1 =
            new PhoneNumber.Builder().SetCountryCode(244).SetNationalNumber(912345678L).Build();
        private static readonly PhoneNumber AO_MOBILE2 =
            new PhoneNumber.Builder().SetCountryCode(244).SetNationalNumber(923456789L).Build();
        private static readonly PhoneNumber AO_FIXED1 =
            new PhoneNumber.Builder().SetCountryCode(244).SetNationalNumber(222333444L).Build();
        // Too short to be valid — UNKNOWN type
        private static readonly PhoneNumber AO_INVALID_NUMBER =
            new PhoneNumber.Builder().SetCountryCode(244).SetNationalNumber(123456L).Build();
        // Prefix 24498 is not present in en/244.txt
        private static readonly PhoneNumber AO_NUMBER_WITH_MISSING_PREFIX =
            new PhoneNumber.Builder().SetCountryCode(244).SetNationalNumber(985000000L).Build();

        // US FIXED_LINE_OR_MOBILE — no carrier data for this prefix in en/1.txt
        private static readonly PhoneNumber US_FIXED_OR_MOBILE =
            new PhoneNumber.Builder().SetCountryCode(1).SetNationalNumber(6502530000L).Build();

        // No carrier data files exist for these country codes
        private static readonly PhoneNumber NUMBER_WITH_INVALID_COUNTRY_CODE =
            new PhoneNumber.Builder().SetCountryCode(999).SetNationalNumber(2423651234L).Build();
        private static readonly PhoneNumber INTERNATIONAL_TOLL_FREE =
            new PhoneNumber.Builder().SetCountryCode(800).SetNationalNumber(12345678L).Build();

        [Fact]
        public void TestGetNameForMobilePortableRegion()
        {
            // UK supports mobile number portability: GetNameForNumber still returns the carrier.
            Assert.Equal("O2", carrierMapper.GetNameForNumber(UK_MOBILE1, Locale.English));
            // No French carrier data for UK — falls back to English.
            Assert.Equal("O2", carrierMapper.GetNameForNumber(UK_MOBILE1, Locale.French));
            // GetSafeDisplayName returns empty because UK has MNP.
            Assert.Equal("", carrierMapper.GetSafeDisplayName(UK_MOBILE1, Locale.English));
        }

        [Fact]
        public void TestGetNameForNonMobilePortableRegion()
        {
            // Angola does not support MNP: both methods return the carrier.
            Assert.Equal("Movicel", carrierMapper.GetNameForNumber(AO_MOBILE1, Locale.English));
            Assert.Equal("Movicel", carrierMapper.GetSafeDisplayName(AO_MOBILE1, Locale.English));
        }

        [Fact]
        public void TestGetNameForFixedLineNumber()
        {
            // Fixed-line numbers are not mobile type: GetNameForNumber returns "".
            Assert.Equal("", carrierMapper.GetNameForNumber(AO_FIXED1, Locale.English));
            Assert.Equal("", carrierMapper.GetNameForNumber(UK_FIXED1, Locale.English));
            // GetNameForValidNumber skips the type check but there is no carrier data for fixed lines.
            Assert.Equal("", carrierMapper.GetNameForValidNumber(AO_FIXED1, Locale.English));
            Assert.Equal("", carrierMapper.GetNameForValidNumber(UK_FIXED1, Locale.English));
        }

        [Fact]
        public void TestGetNameForFixedOrMobileNumber()
        {
            // FIXED_LINE_OR_MOBILE is treated as mobile by GetNameForNumber.
            // No carrier data exists for US prefix 1-6502..., so "" is returned.
            Assert.Equal("", carrierMapper.GetNameForNumber(US_FIXED_OR_MOBILE, Locale.English));
        }

        [Fact]
        public void TestGetNameForNumberWithNoDataFile()
        {
            // No carrier data file for country code 999 or 800.
            Assert.Equal("", carrierMapper.GetNameForNumber(NUMBER_WITH_INVALID_COUNTRY_CODE, Locale.English));
            Assert.Equal("", carrierMapper.GetNameForNumber(INTERNATIONAL_TOLL_FREE, Locale.English));
            Assert.Equal("", carrierMapper.GetNameForValidNumber(NUMBER_WITH_INVALID_COUNTRY_CODE, Locale.English));
            Assert.Equal("", carrierMapper.GetNameForValidNumber(INTERNATIONAL_TOLL_FREE, Locale.English));
        }

        [Fact]
        public void TestGetNameForNumberWithMissingPrefix()
        {
            // Prefix 24498 is absent from en/244.txt — returns "" regardless of number type.
            Assert.Equal("", carrierMapper.GetNameForNumber(AO_NUMBER_WITH_MISSING_PREFIX, Locale.English));
            Assert.Equal("", carrierMapper.GetNameForValidNumber(AO_NUMBER_WITH_MISSING_PREFIX, Locale.English));
        }

        [Fact]
        public void TestGetNameForInvalidNumber()
        {
            // UNKNOWN-type numbers are not mobile, so GetNameForNumber returns "".
            Assert.Equal("", carrierMapper.GetNameForNumber(UK_INVALID_NUMBER, Locale.English));
            Assert.Equal("", carrierMapper.GetNameForNumber(AO_INVALID_NUMBER, Locale.English));
        }

        [Fact]
        public void TestGetNameForValidNumber()
        {
            // GetNameForValidNumber skips type checking and returns the carrier directly.
            Assert.Equal("O2", carrierMapper.GetNameForValidNumber(UK_MOBILE1, Locale.English));
            Assert.Equal("Virgin Mobile", carrierMapper.GetNameForValidNumber(UK_MOBILE2, Locale.English));
            Assert.Equal("Movicel", carrierMapper.GetNameForValidNumber(AO_MOBILE1, Locale.English));
            Assert.Equal("UNITEL", carrierMapper.GetNameForValidNumber(AO_MOBILE2, Locale.English));
        }

        [Fact]
        public void TestGetSafeDisplayName()
        {
            // UK supports MNP — always returns "".
            Assert.Equal("", carrierMapper.GetSafeDisplayName(UK_MOBILE1, Locale.English));
            // Angola does not support MNP — returns the carrier name.
            Assert.Equal("Movicel", carrierMapper.GetSafeDisplayName(AO_MOBILE1, Locale.English));
            // Fixed-line: GetSafeDisplayName calls GetNameForNumber which returns "" for non-mobile type.
            Assert.Equal("", carrierMapper.GetSafeDisplayName(UK_FIXED1, Locale.English));
        }

        [Fact]
        public void TestGetNameFallbackToEnglish()
        {
            // French has no carrier data for UK, so the result falls back to English.
            Assert.Equal("O2", carrierMapper.GetNameForValidNumber(UK_MOBILE1, Locale.French));
            // Korean never falls back to English (zh, ja, ko are excluded).
            Assert.Equal("", carrierMapper.GetNameForValidNumber(UK_MOBILE1, Locale.Korean));
        }
    }
}
