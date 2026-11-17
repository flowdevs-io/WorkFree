using WorkFree.Core;

namespace WorkFree.Tests;

public class FrogSceneTests
{
    private static int Flies(FrogFrame frame) => frame.Kinds.Sum(row => row.Count(c => c == FrogScene.KindFly)) / 3;

    private static void TickUntil(FrogScene scene, Func<bool> done, int limit = 2000)
    {
        for (var i = 0; i < limit && !done(); i++)
        {
            scene.Tick();
            var frame = scene.Render();
            Assert.Equal(FrogScene.Height, frame.Lines.Length);
            Assert.All(frame.Lines, line => Assert.Equal(FrogScene.Width, line.Length));
            Assert.All(frame.Kinds, row => Assert.Equal(FrogScene.Width, row.Length));
        }
    }

    [Fact]
    public void Frog_eats_a_fly_only_after_its_removal_finishes()
    {
        var scene = new FrogScene();
        scene.StartMeal(3);

        // No removal has finished: the frog reaches the first fly and holds it on its tongue.
        TickUntil(scene, () => false, limit: 200);
        Assert.Equal(3, scene.FliesLeft);
        Assert.False(scene.CaughtUp);
        Assert.Contains(scene.Render().Kinds, row => row.Contains(FrogScene.KindTongue));

        scene.Eat();
        TickUntil(scene, () => scene.FliesLeft == 2);
        Assert.Equal(2, scene.FliesLeft);

        scene.Eat();
        scene.Eat();
        scene.Eat(); // more than there are flies: ignored
        TickUntil(scene, () => scene.CaughtUp);
        Assert.True(scene.CaughtUp);
        Assert.Equal(0, Flies(scene.Render()));
    }

    [Fact]
    public void Frog_catches_up_quickly_when_many_removals_finish_at_once()
    {
        var scene = new FrogScene();
        scene.StartMeal(40);
        for (var i = 0; i < 40; i++) scene.Eat();

        var ticks = 0;
        while (!scene.CaughtUp && ticks < 5000) { scene.Tick(); ticks++; }

        Assert.True(scene.CaughtUp);
        Assert.True(ticks < 40 * 8, $"took {ticks} ticks");
    }

    [Fact]
    public void Search_and_celebration_frames_are_well_formed()
    {
        var scene = new FrogScene();
        scene.StartSearch();
        TickUntil(scene, () => false, limit: 150);
        Assert.True(scene.CaughtUp);
        Assert.Contains(scene.Render().Kinds, row => row.Contains(FrogScene.KindFrog));

        scene.Celebrate("12 worktree(s) gone, 3.4 GB freed, and a note far too long to fit beside the frog at all");
        TickUntil(scene, () => false, limit: 30);
        Assert.Contains(scene.Render().Lines, line => line.Contains("*burp*"));
        Assert.Contains(scene.Render().Lines, line => line.Contains("12 worktree(s) gone"));
    }
}
