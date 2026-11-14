using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class InputReaderLifecyclePlayModeTests
{
    private ScriptableObject reader;
    private Type readerType;
    private Keyboard keyboard;
    private InputTestFixture inputFixture;

    [SetUp]
    public void SetUp()
    {
        inputFixture = new InputTestFixture();
        inputFixture.Setup();
        readerType = Type.GetType("InputReader, Assembly-CSharp", true);
        reader = ScriptableObject.CreateInstance(readerType);
        keyboard = InputSystem.AddDevice<Keyboard>();
    }

    [TearDown]
    public void TearDown()
    {
        readerType.GetMethod("DisablePlayerActions").Invoke(reader, null);
        object actions = readerType.GetField("inputActions").GetValue(reader);
        (actions as IDisposable)?.Dispose();
        InputSystem.RemoveDevice(keyboard);
        UnityEngine.Object.DestroyImmediate(reader);
        inputFixture.TearDown();
    }

    [UnityTest]
    public IEnumerator JumpCallbacksWorkAfterDisablingAndReenablingInput()
    {
        int presses = 0;
        int releases = 0;
        UnityAction<bool> callback = pressed =>
        {
            if (pressed) presses++;
            else releases++;
        };
        readerType.GetEvent("Jump").AddEventHandler(reader, callback);

        Enable();
        yield return null;
        SetKeys(Key.Space);
        Assert.That(presses, Is.EqualTo(1), "The first enable must deliver Jump.");
        SetKeys();
        Assert.That(releases, Is.EqualTo(1));

        readerType.GetMethod("DisablePlayerActions").Invoke(reader, null);
        Enable();
        yield return null;
        SetKeys(Key.W, Key.Space);

        Vector2 direction = (Vector2)readerType.GetProperty("Direction").GetValue(reader);
        Assert.That(direction.y, Is.GreaterThan(0.9f), "Movement polling is still live after re-enable.");
        Assert.That(presses, Is.EqualTo(2),
            "Jump callbacks must be rebound even when an existing input action asset is reused.");
        SetKeys();
        Assert.That(releases, Is.EqualTo(2), "Release must also work so jumping can unlock.");
    }

    [UnityTest]
    public IEnumerator RepeatedEnableDoesNotDuplicateJumpCallbacks()
    {
        int presses = 0;
        UnityAction<bool> callback = pressed => { if (pressed) presses++; };
        readerType.GetEvent("Jump").AddEventHandler(reader, callback);

        Enable();
        Enable();
        Enable();
        yield return null;
        SetKeys(Key.Space);

        Assert.That(presses, Is.EqualTo(1));
        SetKeys();
    }

    private void Enable() => readerType.GetMethod("EnablePlayerActions").Invoke(reader, null);

    private void SetKeys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
    }
}
