using VisionaryRouter.Core.Models;
using VisionaryRouter.Core.Protocol;

namespace VisionaryRouter.Tests;

public class CommandBuilderTests
{
    [Fact]
    public void Route_produces_the_documented_command()
    {
        Assert.Equal(
            "CMD=START&UNIT.ID=ALL&STREAM.HOST=192.168.8.101&VW.ACTIVE=FALSE&STREAM.CONNECT=TRUE&CMD=END",
            VisionaryCommand.Route("192.168.8.101"));
    }

    [Fact]
    public void Status_query_produces_the_documented_command()
    {
        Assert.Equal(
            "CMD=START&UNIT.ID=ALL&QUERY.STATUS=TRUE&CMD=END",
            VisionaryCommand.StatusQuery());
    }

    [Fact]
    public void Current_route_query_asks_for_stream_host()
    {
        Assert.Equal(
            "CMD=START&UNIT.ID=ALL&QUERY.KEY=STREAM.HOST&CMD=END",
            VisionaryCommand.QueryCurrentRoute());
    }

    [Fact]
    public void Identify_produces_the_documented_command()
    {
        Assert.Equal(
            "CMD=START&UNIT.ID=ALL&UNIT.FU=TRUE&CMD=END",
            VisionaryCommand.Identify());
    }

    [Theory]
    [InlineData("SAVE=TRUE")]
    [InlineData("UNIT.SAVE=TRUE")]
    [InlineData("UNIT.REBOOT=TRUE")]
    public void Refuses_to_build_commands_that_write_endpoint_configuration(string field)
    {
        // Routing is meant to be volatile. Writing it to nonvolatile storage on every switch
        // wears the endpoint and is not what the operator asked for.
        Assert.Throws<InvalidOperationException>(() => VisionaryCommand.Build("UNIT.ID=ALL", field));
    }

    [Fact]
    public void Route_rejects_an_empty_encoder_address()
    {
        Assert.Throws<ArgumentException>(() => VisionaryCommand.Route("  "));
    }
}

public class ResponseParserTests
{
    [Fact]
    public void Parses_a_documented_query_response()
    {
        var response = VisionaryResponse.Parse("&STREAM.HOST=192.168.8.101&API.STATUS=SUCCESS_QUERY");

        Assert.Equal("192.168.8.101", response.Get(VisionaryKeys.StreamHost));
        Assert.Equal("SUCCESS_QUERY", response.ApiStatus);
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public void Parses_a_switch_acknowledgement()
    {
        var response = VisionaryResponse.Parse("&STREAM.CONNECT=queue_next_switch&API.STATUS=SUCCESS");

        Assert.Equal("queue_next_switch", response.Get(VisionaryKeys.StreamConnect));
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public void Keeps_keys_it_does_not_understand()
    {
        // Firmware updates add fields. Dropping them would mean a newer endpoint silently
        // loses information we might want to show.
        var response = VisionaryResponse.Parse("&SOMETHING.NEW=42&API.STATUS=SUCCESS");

        Assert.Equal("42", response.Get("SOMETHING.NEW"));
    }

    [Fact]
    public void Keys_are_case_sensitive()
    {
        var response = VisionaryResponse.Parse("&STREAM.HOST=10.0.0.1");

        Assert.Equal("10.0.0.1", response.Get("STREAM.HOST"));
        Assert.Null(response.Get("stream.host"));
    }

    [Fact]
    public void Splits_only_on_the_first_equals_sign()
    {
        var response = VisionaryResponse.Parse("&UNIT.NAME=Room=A&API.STATUS=SUCCESS");

        Assert.Equal("Room=A", response.Get("UNIT.NAME"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("&&&")]
    [InlineData("garbage without any delimiters")]
    [InlineData("=novalue")]
    public void Malformed_input_yields_an_empty_result_rather_than_throwing(string raw)
    {
        var response = VisionaryResponse.Parse(raw);

        Assert.False(response.IsSuccess);
        Assert.Null(response.ApiStatus);
    }

    [Fact]
    public void Null_input_is_tolerated()
    {
        var response = VisionaryResponse.Parse(null);

        Assert.Empty(response.Values);
        Assert.Equal(string.Empty, response.Raw);
    }

    [Fact]
    public void Raw_text_is_preserved_for_diagnostics()
    {
        const string raw = "&STREAM.HOST=10.0.0.5&API.STATUS=SUCCESS_QUERY";

        Assert.Equal(raw, VisionaryResponse.Parse(raw).Raw);
    }

    [Fact]
    public void A_failure_status_is_not_reported_as_success()
    {
        var response = VisionaryResponse.Parse("&API.STATUS=FAILURE_UNKNOWN_KEY");

        Assert.False(response.IsSuccess);
    }
}

public class DeviceClassifierTests
{
    [Theory]
    [InlineData("E4100")]
    [InlineData("E4200")]
    [InlineData("E5100")]
    [InlineData("E5200")]
    [InlineData("DuetE-5")]
    public void Encoders_are_recognised(string model)
    {
        Assert.Equal(DeviceType.Encoder, DeviceClassifier.FromModel(model));
    }

    [Theory]
    [InlineData("D4100")]
    [InlineData("D4200")]
    [InlineData("D5100")]
    [InlineData("D5200")]
    [InlineData("DuetD-5")]
    public void Decoders_are_recognised(string model)
    {
        Assert.Equal(DeviceType.Decoder, DeviceClassifier.FromModel(model));
    }

    [Fact]
    public void A_duet_without_a_role_letter_is_left_for_the_user()
    {
        // Duet hardware can be either, so guessing would be worse than asking.
        Assert.Equal(DeviceType.Unknown, DeviceClassifier.FromModel("Duet-5"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("X9000")]
    public void Anything_unrecognised_is_unknown(string? model)
    {
        Assert.Equal(DeviceType.Unknown, DeviceClassifier.FromModel(model));
    }

    [Fact]
    public void A_model_string_that_arrives_with_whitespace_still_classifies()
    {
        Assert.Equal(DeviceType.Encoder, DeviceClassifier.FromModel("  E5200 "));
    }
}
