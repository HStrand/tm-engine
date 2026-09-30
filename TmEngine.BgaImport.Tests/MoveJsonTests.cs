using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using tm_engine.Serialization;
using TmEngine.Domain.Moves;

namespace TmEngine.BgaImport.Tests;

public class MoveJsonTests
{
    private static readonly JsonSerializerSettings Json = SerializationSettings.Create();

    [Fact]
    public void Heat_IsReadFromAndWrittenToAnyMove()
    {
        var move = JsonConvert.DeserializeObject<Move>("""{"type":"Greenery","playerId":1,"location":{"col":5,"row":9},"heat":12}""", Json);

        Assert.Equal(12, Assert.IsType<GreeneryMove>(move).Heat);
        Assert.Equal(12, JObject.Parse(JsonConvert.SerializeObject(move, typeof(Move), Json)).Value<int>("heat"));
    }

    [Fact]
    public void Heat_IsLeftToTheEngine_WhenTheMoveDoesNotGiveIt()
    {
        var move = JsonConvert.DeserializeObject<Move>("""{"type":"PowerPlant","playerId":0}""", Json);

        Assert.Null(move!.Heat);
    }
}
