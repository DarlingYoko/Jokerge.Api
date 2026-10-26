using AutoMapper;
using Gml.Dto.Profile;
using Gml.Models;
using Gml.Web.Api.Core.MappingProfiles;
using Gml.Web.Api.Core.Validation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gml.WebApi.Tests;

public class ProfileDiscordFieldsTests
{
    private static ProfileUpdateDto ValidUpdateDto() => new()
    {
        OriginalName = "profile",
        Name = "profile",
        DisplayName = "Profile",
        Description = "Description"
    };

    [Test]
    public void ProfileReadDto_ContainsDiscordFields()
    {
        var mapper = new MapperConfiguration(c => c.AddProfile<ProfileMapper>(), NullLoggerFactory.Instance)
            .CreateMapper();

        var profile = new GameProfile
        {
            Name = "profile",
            DisplayName = "Profile",
            DiscordName = "Jokerge: Survival ✦",
            DiscordImage = "https://example.com/icon.png"
        };

        var dto = mapper.Map<ProfileReadDto>(profile);

        Assert.Multiple(() =>
        {
            Assert.That(dto.DiscordName, Is.EqualTo("Jokerge: Survival ✦"));
            Assert.That(dto.DiscordImage, Is.EqualTo("https://example.com/icon.png"));
        });
    }

    [Test]
    public void UpdateValidator_AllowsEmptyDiscordFields()
    {
        var result = new ProfileUpdateDtoValidator().Validate(ValidUpdateDto());

        Assert.That(result.IsValid, Is.True, string.Join("; ", result.Errors));
    }

    [Test]
    public void UpdateValidator_RejectsTooLongDiscordName()
    {
        var dto = ValidUpdateDto();
        dto.DiscordName = new string('a', 129);

        var result = new ProfileUpdateDtoValidator().Validate(dto);

        Assert.That(result.Errors.Select(e => e.PropertyName), Does.Contain(nameof(ProfileUpdateDto.DiscordName)));
    }

    [Test]
    public void UpdateValidator_RejectsTooLongDiscordImage()
    {
        var dto = ValidUpdateDto();
        dto.DiscordImage = "https://example.com/" + new string('a', 256);

        var result = new ProfileUpdateDtoValidator().Validate(dto);

        Assert.That(result.Errors.Select(e => e.PropertyName), Does.Contain(nameof(ProfileUpdateDto.DiscordImage)));
    }
}
