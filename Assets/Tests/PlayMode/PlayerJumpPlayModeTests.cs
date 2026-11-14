using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class PlayerJumpPlayModeTests
{
    private static readonly Vector3 Origin = new Vector3(1200f, 1000f, 1200f);
    private readonly List<GameObject> gameObjects = new List<GameObject>();
    private readonly List<Material> materials = new List<Material>();
    private Type controllerType;
    private Type moverType;
    private Type readerType;
    private Type paintingType;
    private ScriptableObject reader;
    private Keyboard keyboard;
    private InputTestFixture inputFixture;

    [SetUp]
    public void SetUp()
    {
        inputFixture = new InputTestFixture();
        inputFixture.Setup();
        controllerType = Type.GetType("AdvancedController.PlayerController, Assembly-CSharp", true);
        moverType = Type.GetType("AdvancedController.PlayerMover, Assembly-CSharp", true);
        readerType = Type.GetType("InputReader, Assembly-CSharp", true);
        paintingType = Type.GetType("Painting, Assembly-CSharp", true);
        keyboard = InputSystem.AddDevice<Keyboard>();
        reader = ScriptableObject.CreateInstance(readerType);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (keyboard != null && keyboard.added)
        {
            QueueKeys();
            yield return null;
        }

        // Controllers must tear down while their shared InputReader still exists.
        foreach (GameObject item in gameObjects)
        {
            if (item != null) UnityEngine.Object.Destroy(item);
        }
        gameObjects.Clear();
        yield return null;

        if (reader != null)
        {
            readerType.GetMethod("DisablePlayerActions").Invoke(reader, null);
            object actions = readerType.GetField("inputActions").GetValue(reader);
            (actions as IDisposable)?.Dispose();
            UnityEngine.Object.Destroy(reader);
            reader = null;
        }

        foreach (Material material in materials)
        {
            if (material != null) UnityEngine.Object.Destroy(material);
        }
        materials.Clear();
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        keyboard = null;
        yield return null;
        inputFixture.TearDown();
    }

    [UnityTest]
    public IEnumerator SpaceMakesFullControllerJumpFromOrdinaryFloor()
    {
        CreateFloor();
        Component controller = CreatePlayer();
        yield return WaitForGrounded(controller);

        yield return AssertKeyboardJump(controller);
    }

    [UnityTest]
    public IEnumerator SpaceMakesFullControllerJumpWhilePaintingFloorKeepsRippling()
    {
        Component painting = CreatePaintingFloor();
        Component controller = CreatePlayer();
        yield return WaitForGrounded(controller);

        // Standing contact must sustain a pulse longer than its configured duration.
        for (int i = 0; i < 12; i++)
        {
            yield return new WaitForFixedUpdate();
            var properties = new MaterialPropertyBlock();
            painting.GetComponent<Renderer>().GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_RippleStrength"), Is.GreaterThan(0f),
                "The full controller's ground sensor must continuously refresh the painting ripple.");
            yield return null;
        }

        yield return AssertKeyboardJump(controller);
    }

    [UnityTest]
    public IEnumerator ReusedInputReaderStillMovesAndJumpsAfterDisableAndEnable()
    {
        // Reuse the same ScriptableObject as a player respawn or repeated play session.
        readerType.GetMethod("EnablePlayerActions").Invoke(reader, null);
        readerType.GetMethod("DisablePlayerActions").Invoke(reader, null);
        CreateFloor();
        Component controller = CreatePlayer();
        yield return WaitForGrounded(controller);
        Rigidbody body = controller.GetComponent<Rigidbody>();
        float startingZ = body.position.z;

        QueueKeys(Key.W);
        yield return null;
        for (int i = 0; i < 6; i++)
        {
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        Vector2 direction = (Vector2)readerType.GetProperty("Direction").GetValue(reader);
        Assert.That(direction.y, Is.GreaterThan(0.9f), "The reused reader must receive real keyboard input.");
        Assert.That(body.position.z - startingZ, Is.GreaterThan(0.2f),
            "Polling movement can work even if the reused reader has lost jump callbacks.");
        QueueKeys();
        yield return null;
        yield return new WaitForFixedUpdate();
        yield return null;

        yield return AssertKeyboardJump(controller);
    }

    private IEnumerator WaitForGrounded(Component controller)
    {
        Physics.SyncTransforms();
        Component mover = controller.GetComponent(moverType);
        for (int i = 0; i < 15; i++)
        {
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        Assert.That((bool)moverType.GetMethod("IsGrounded").Invoke(mover, null), Is.True,
            "The real controller must settle on the test floor before input is pressed.");
        Assert.That(Mathf.Abs(controller.GetComponent<Rigidbody>().linearVelocity.y), Is.LessThan(0.2f));
    }

    private IEnumerator AssertKeyboardJump(Component controller)
    {
        Rigidbody body = controller.GetComponent<Rigidbody>();
        float startingY = body.position.y;
        float maximumY = startingY;
        float maximumUpwardVelocity = body.linearVelocity.y;
        QueueKeys(Key.Space);
        yield return null;

        for (int i = 0; i < 16; i++)
        {
            yield return new WaitForFixedUpdate();
            maximumY = Mathf.Max(maximumY, body.position.y);
            maximumUpwardVelocity = Mathf.Max(maximumUpwardVelocity, body.linearVelocity.y);
            yield return null;
        }

        QueueKeys();
        yield return null;
        Assert.That(maximumUpwardVelocity, Is.GreaterThan(5f),
            "Space must reach the production InputReader and apply a real upward rigidbody velocity.");
        Assert.That(maximumY - startingY, Is.GreaterThan(0.3f),
            "The player must physically leave the floor, rather than only changing a private jump flag.");
    }

    private Component CreatePlayer()
    {
        GameObject player = Track(new GameObject("Full-controller jump regression player"));
        player.SetActive(false);
        player.transform.position = Origin + Vector3.up;
        Component controller = player.AddComponent(controllerType);
        SetField(controller, "input", reader);
        player.SetActive(true);
        return controller;
    }

    private void CreateFloor()
    {
        GameObject floor = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        floor.name = "Jump regression floor";
        floor.transform.position = Origin + Vector3.down * 0.25f;
        floor.transform.localScale = new Vector3(16f, 0.5f, 16f);
    }

    private Component CreatePaintingFloor()
    {
        GameObject floor = Track(GameObject.CreatePrimitive(PrimitiveType.Quad));
        floor.SetActive(false);
        floor.name = "Jump regression painting floor";
        floor.transform.position = Origin;
        floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        floor.transform.localScale = Vector3.one * 16f;
        Shader shader = Shader.Find("Shader Graphs/Mario 64 Painting");
        Assert.That(shader, Is.Not.Null, "Use the project's real painting shader.");
        var material = new Material(shader);
        materials.Add(material);
        floor.GetComponent<Renderer>().sharedMaterial = material;
        Component painting = floor.AddComponent(paintingType);
        SetField(painting, "rippleTime", 0.05f);
        SetField(painting, "allowPlayerPassThrough", false);
        floor.SetActive(true);
        return painting;
    }

    private void QueueKeys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    }

    private static void SetField(Component target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Missing serialized configuration: " + name);
        field.SetValue(target, value);
    }

    private GameObject Track(GameObject item)
    {
        gameObjects.Add(item);
        return item;
    }
}
