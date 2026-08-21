using System;
using System.Collections.Generic;
using Xunit;

namespace Mapsicle.Tests
{
    /// <summary>
    /// What happens when the source of a mapping is not trusted.
    /// </summary>
    /// <remarks>
    /// The honest answer, pinned here so nobody has to infer it from the implementation: a mapper
    /// copies every matching property it can, and that is the whole point of one. Pointed at a
    /// request body it will happily set any property whose name lines up, including one the caller
    /// had no business setting. This is true of AutoMapper and of every convention mapper.
    ///
    /// So these tests exist to state the boundary rather than to claim a defence Mapsicle does not
    /// have:
    ///
    ///   - over-posting works, and the safe pattern is to map untrusted input into a DTO that
    ///     contains only the fields a caller may set, never straight into a domain entity;
    ///   - <see cref="IgnoreMapAttribute"/> is a real control and is honoured on every entry point,
    ///     which is what makes the DTO pattern enforceable when a shared type is unavoidable;
    ///   - values are copied, never interpreted, so nothing in a string is executed or parsed;
    ///   - a value of the wrong type is dropped rather than coerced or thrown.
    ///
    /// A test here failing means a documented security property changed.
    /// </remarks>
    [Collection("StaticMapperTests")]
    public class UntrustedInputTests
    {
        // ---- Over-posting -----------------------------------------------------------------------

        // Documents the exposure rather than asserting a defence. Mapping a hostile dictionary
        // straight into an entity sets whatever lines up, so the guidance is to map into a DTO.
        [Fact]
        public void MappingUntrustedKeysIntoAnEntity_SetsEveryMatchingProperty()
        {
            Mapper.ClearCache();

            var hostile = new Dictionary<string, object?>
            {
                ["Email"] = "user@example.com",
                ["IsAdmin"] = true,
                ["Balance"] = 999_999m,
            };

            var entity = hostile.MapTo<UntrustedAccount>();

            Assert.Equal("user@example.com", entity!.Email);
            Assert.True(entity.IsAdmin);
            Assert.Equal(999_999m, entity.Balance);
        }

        // The recommended pattern, asserted so the README's advice is backed by a running test: a
        // DTO holding only the settable fields cannot carry a privilege field at all.
        [Fact]
        public void MappingUntrustedKeysIntoADto_CannotReachFieldsTheDtoDoesNotHave()
        {
            Mapper.ClearCache();

            var hostile = new Dictionary<string, object?>
            {
                ["Email"] = "user@example.com",
                ["IsAdmin"] = true,
                ["Balance"] = 999_999m,
            };

            var dto = hostile.MapTo<UntrustedAccountDto>();
            var entity = new UntrustedAccount { IsAdmin = false, Balance = 10m };
            dto.Map(entity);

            Assert.Equal("user@example.com", entity.Email);
            Assert.False(entity.IsAdmin);
            Assert.Equal(10m, entity.Balance);
        }

        // ---- IgnoreMap as an enforceable control ------------------------------------------------

        [Fact]
        public void IgnoreMap_IsHonouredOnTheDictionaryPath()
        {
            Mapper.ClearCache();

            var hostile = new Dictionary<string, object?>
            {
                ["Email"] = "user@example.com",
                ["InternalNote"] = "injected",
            };

            var entity = hostile.MapTo<UntrustedAccount>();

            Assert.Equal("user@example.com", entity!.Email);
            Assert.Equal(string.Empty, entity.InternalNote);
        }

        [Fact]
        public void IgnoreMap_IsHonouredOnTheObjectPath()
        {
            Mapper.ClearCache();

            var source = new UntrustedIncoming { Email = "user@example.com", InternalNote = "injected" };

            var entity = source.MapTo<UntrustedAccount>();

            Assert.Equal(string.Empty, entity!.InternalNote);
        }

        [Fact]
        public void IgnoreMap_IsHonouredByAMapperInstance()
        {
            using var mapper = MapperFactory.Create();

            var source = new UntrustedIncoming { Email = "user@example.com", InternalNote = "injected" };

            var entity = mapper.MapTo<UntrustedAccount>(source);

            Assert.Equal(string.Empty, entity!.InternalNote);
        }

        // ---- Values are copied, never interpreted -----------------------------------------------

        [Theory]
        [InlineData("'; DROP TABLE Users; --")]
        [InlineData("<script>alert(1)</script>")]
        [InlineData("{0}{1}{2}")]
        [InlineData("${jndi:ldap://example.invalid/a}")]
        [InlineData("../../../../etc/passwd")]
        [InlineData("\0embedded null")]
        public void HostileStringValues_ArriveByteForByte(string payload)
        {
            Mapper.ClearCache();

            var dest = new UntrustedIncoming { Email = payload }.MapTo<UntrustedAccountDto>();

            // Unchanged is the correct outcome. A mapper that sanitised here would be doing
            // something the caller did not ask for and cannot see.
            Assert.Equal(payload, dest!.Email);
        }

        [Fact]
        public void AVeryLongValue_IsNeitherTruncatedNorRejected()
        {
            Mapper.ClearCache();
            var payload = new string('a', 1_000_000);

            var dest = new UntrustedIncoming { Email = payload }.MapTo<UntrustedAccountDto>();

            Assert.Equal(1_000_000, dest!.Email.Length);
        }

        // ---- Wrong types are dropped, not coerced and not thrown --------------------------------

        [Fact]
        public void ADictionaryValueOfTheWrongType_LeavesTheDestinationDefault()
        {
            Mapper.ClearCache();

            var hostile = new Dictionary<string, object?> { ["Balance"] = "not-a-number" };

            var entity = hostile.MapTo<UntrustedAccount>();

            // Not an exception, and not a coerced value: an attacker cannot use a type mismatch to
            // crash a request handler, and cannot smuggle a value through a loose conversion.
            Assert.Equal(0m, entity!.Balance);
        }

        [Fact]
        public void ADictionaryNullValue_ForAValueType_LeavesTheDestinationDefault()
        {
            Mapper.ClearCache();

            var hostile = new Dictionary<string, object?> { ["Balance"] = null };

            var entity = hostile.MapTo<UntrustedAccount>();

            Assert.Equal(0m, entity!.Balance);
        }

        [Fact]
        public void UnknownKeys_AreIgnoredRatherThanThrowing()
        {
            Mapper.ClearCache();

            var hostile = new Dictionary<string, object?>
            {
                ["Email"] = "user@example.com",
                ["NoSuchProperty"] = "whatever",
                [""] = "empty key",
            };

            var entity = hostile.MapTo<UntrustedAccount>();

            Assert.Equal("user@example.com", entity!.Email);
        }

        #region Types

        public class UntrustedAccount
        {
            public string Email { get; set; } = "";
            public bool IsAdmin { get; set; }
            public decimal Balance { get; set; }

            [IgnoreMap]
            public string InternalNote { get; set; } = "";
        }

        /// <summary>Only what a caller is allowed to set.</summary>
        public class UntrustedAccountDto
        {
            public string Email { get; set; } = "";
        }

        public class UntrustedIncoming
        {
            public string Email { get; set; } = "";
            public string InternalNote { get; set; } = "";
        }

        #endregion
    }
}
