using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class PaintingRipplePlayModeTests
{
    private const string StrengthProperty = "_RippleStrength";
    private const string CenterProperty = "_RippleCenter";
    private static readonly Vector3 Origin = new Vector3(1000f, 1000f, 1000f);
    private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();
    private Type paintingType;
    private Type moverType;

    [SetUp]
    public void SetUp()
    {
        paintingType = Type.GetType("Painting, Assembly-CSharp", true);
        moverType = Type.GetType("AdvancedController.PlayerMover, Assembly-CSharp", true);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (UnityEngine.Object item in createdObjects)
        {
            if (item != null) UnityEngine.Object.Destroy(item);
        }
        createdObjects.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator StartupOverridesNonzeroMaterialStrengthWithoutChangingSharedMaterial()
    {
        Component painting = CreatePainting();
        Material material = painting.GetComponent<Renderer>().sharedMaterial;
        yield return null;

        Assert.That(Strength(painting), Is.Zero, "An untouched painting must start still.");
        Assert.That(material.GetFloat(StrengthProperty), Is.EqualTo(0.75f),
            "Initialization must not modify the shared material asset.");
    }

    [UnityTest]
    public IEnumerator PulseFadesAllTheWayToExactZero()
    {
        Component painting = CreatePainting();
        Play(painting, Origin + new Vector3(1.25f, -0.5f, 0f));
        Assert.That(Strength(painting), Is.GreaterThan(0f));
        yield return new WaitForSeconds(0.3f);
        Assert.That(Strength(painting), Is.Zero, "A finished ripple must leave no residual displacement.");
    }

    [UnityTest]
    public IEnumerator DisableAndReenableClearsAnActivePulse()
    {
        Component painting = CreatePainting();
        Play(painting, Origin);
        Assert.That(Strength(painting), Is.GreaterThan(0f));
        painting.gameObject.SetActive(false);
        Assert.That(Strength(painting), Is.Zero);
        painting.gameObject.SetActive(true);
        yield return null;
        Assert.That(Strength(painting), Is.Zero);
    }

    [UnityTest]
    public IEnumerator SharedMaterialPaintingsKeepIndependentRippleState()
    {
        Component first = CreatePainting();
        Material shared = first.GetComponent<Renderer>().sharedMaterial;
        Component second = CreatePainting(sharedMaterial: shared, offset: Vector3.right * 15f);
        Vector3 hitPoint = Origin + Vector3.right;
        Play(first, hitPoint);
        yield return null;

        Assert.That(Strength(first), Is.GreaterThan(0f));
        Assert.That(Strength(second), Is.Zero);
        Assert.That(Vector3.Distance(Center(first), hitPoint), Is.LessThan(0.001f));
        Assert.That(shared.GetFloat(StrengthProperty), Is.EqualTo(0.75f));
    }

    [UnityTest]
    public IEnumerator PublicPulseUsesWorldSpacePositionOnTransformedPainting()
    {
        Component painting = CreatePainting();
        painting.transform.rotation = Quaternion.Euler(25f, 45f, 10f);
        Vector3 hitPoint = painting.transform.TransformPoint(new Vector3(0.2f, -0.3f, 0f));
        Play(painting, hitPoint);
        yield return null;
        Assert.That(Vector3.Distance(Center(painting), hitPoint), Is.LessThan(0.001f));
    }

    [UnityTest]
    public IEnumerator ZeroDurationNeverLeavesANonfiniteOrPersistentRipple()
    {
        Component painting = CreatePainting(duration: 0f);
        Play(painting, Origin);
        yield return null;
        float strength = Strength(painting);
        Assert.That(float.IsNaN(strength) || float.IsInfinity(strength), Is.False);
        Assert.That(strength, Is.Zero);
    }

    [UnityTest]
    public IEnumerator ObliqueMouseRayUsesVisibleSurfaceInsteadOfTriggerEntry()
    {
        Component painting = CreatePainting(passThrough: true);
        Vector3 expectedPoint = Origin + new Vector3(1f, 0.5f, 0f);
        Vector3 rayOrigin = Origin + new Vector3(-2f, 1f, -3f);
        Physics.SyncTransforms();

        PointerRay(painting, new Ray(rayOrigin, expectedPoint - rayOrigin));

        Assert.That(Strength(painting), Is.GreaterThan(0f));
        Assert.That(Vector3.Distance(Center(painting), expectedPoint), Is.LessThan(0.001f));
        yield return null;
    }

    [UnityTest]
    public IEnumerator MouseRayHittingTriggerSideButMissingVisibleQuadDoesNotRipple()
    {
        Component painting = CreatePainting(passThrough: true);
        Vector3 rayOrigin = Origin + Vector3.back * 2f;
        Vector3 outsidePoint = Origin + Vector3.right * 4.2f;
        Physics.SyncTransforms();

        PointerRay(painting, new Ray(rayOrigin, outsidePoint - rayOrigin));

        Assert.That(Strength(painting), Is.Zero);
        yield return null;
    }

    [UnityTest]
    public IEnumerator MouseRayOnlyActivatesThePaintingItHits()
    {
        Component first = CreatePainting(passThrough: true);
        Component second = CreatePainting(passThrough: true, offset: Vector3.right * 15f);
        Ray ray = new Ray(Origin + Vector3.back * 3f, Vector3.forward);
        Physics.SyncTransforms();

        PointerRay(first, ray);
        PointerRay(second, ray);

        Assert.That(Strength(first), Is.GreaterThan(0f));
        Assert.That(Strength(second), Is.Zero);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PlayerCrossesPaintingFromBackAndRipplesAtOffCenterContact()
    {
        yield return AssertPhysicalCrossing(-1f);
    }

    [UnityTest]
    public IEnumerator PlayerCrossesPaintingFromFrontAndRipplesAtOffCenterContact()
    {
        yield return AssertPhysicalCrossing(1f);
    }

    [UnityTest]
    public IEnumerator NonPlayerRigidbodyDoesNotActivatePassThroughPainting()
    {
        Component painting = CreatePainting(passThrough: true);
        GameObject other = Track(GameObject.CreatePrimitive(PrimitiveType.Sphere));
        other.transform.position = Origin;
        Rigidbody body = other.AddComponent<Rigidbody>();
        body.useGravity = false;
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        Assert.That(Strength(painting), Is.Zero);
    }

    [UnityTest]
    public IEnumerator StandingPlayerKeepsRipplingBeyondPulseDurationWithFeetAboveSurface()
    {
        Component painting = CreatePainting(horizontal: true, duration: 0.05f);
        Component mover = CreatePlayer(Origin + new Vector3(1.2f, 1f, -0.7f));
        Physics.SyncTransforms();
        Collider collider = mover.GetComponent<Collider>();
        Assert.That(collider.bounds.min.y, Is.GreaterThan(Origin.y + 0.1f),
            "The test must exercise the controller's ground sensor, not trigger overlap.");

        for (int i = 0; i < 12; i++)
        {
            CheckGround(mover);
            Assert.That((bool)moverType.GetMethod("IsGrounded").Invoke(mover, null), Is.True);
            Assert.That(Strength(painting), Is.GreaterThan(0f), "Ground contact must refresh the ripple.");
            yield return new WaitForFixedUpdate();
        }

        Assert.That(Vector3.Distance(Center(painting), Origin + new Vector3(1.2f, 0f, -0.7f)),
            Is.LessThan(0.01f), "Ripple center must be the ground hit, not the player's body center.");
    }

    [UnityTest]
    public IEnumerator StandingRippleFollowsGroundHitAndFadesAfterPlayerLeaves()
    {
        Component painting = CreatePainting(horizontal: true, duration: 0.05f);
        Component mover = CreatePlayer(Origin + new Vector3(-1f, 1f, 0.4f));
        Physics.SyncTransforms();
        CheckGround(mover);
        Assert.That(Vector3.Distance(Center(painting), Origin + new Vector3(-1f, 0f, 0.4f)), Is.LessThan(0.01f));

        mover.transform.position = Origin + new Vector3(1.5f, 1f, -1f);
        Physics.SyncTransforms();
        CheckGround(mover);
        Assert.That(Vector3.Distance(Center(painting), Origin + new Vector3(1.5f, 0f, -1f)), Is.LessThan(0.01f));

        mover.transform.position = Origin + Vector3.up * 5f;
        Physics.SyncTransforms();
        CheckGround(mover);
        Assert.That((bool)moverType.GetMethod("IsGrounded").Invoke(mover, null), Is.False);
        yield return new WaitForSeconds(0.15f);
        Assert.That(Strength(painting), Is.Zero);
    }

    private IEnumerator AssertPhysicalCrossing(float startingSide)
    {
        Component painting = CreatePainting(passThrough: true, duration: 0.2f);
        Component mover = CreatePlayer(Origin + new Vector3(1.1f, -0.6f, startingSide * 1.5f));
        Rigidbody body = mover.GetComponent<Rigidbody>();
        body.linearVelocity = Vector3.forward * (-startingSide * 2f);
        Physics.SyncTransforms();
        bool sawRipple = false;
        float deadline = Time.time + 1.6f;
        while (Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            if (Strength(painting) > 0f)
            {
                sawRipple = true;
                Vector3 contact = Center(painting);
                Assert.That(contact.x, Is.EqualTo(Origin.x + 1.1f).Within(0.02f));
                Assert.That(contact.y, Is.EqualTo(mover.GetComponent<Collider>().bounds.center.y).Within(0.02f));
                Assert.That(contact.z, Is.EqualTo(Origin.z).Within(0.001f));
            }
        }

        Assert.That(sawRipple, Is.True, "Real physics trigger contact must activate the player ripple.");
        Assert.That((body.position.z - Origin.z) * startingSide, Is.LessThan(-0.5f),
            "The original mesh collider must not block pass-through movement.");
    }

    private Component CreatePainting(bool horizontal = false, bool passThrough = false,
        float duration = 0.15f, Material sharedMaterial = null, Vector3 offset = default)
    {
        GameObject quad = Track(GameObject.CreatePrimitive(PrimitiveType.Quad));
        quad.SetActive(false);
        quad.name = "Painting regression test quad";
        quad.transform.position = Origin + offset;
        quad.transform.localScale = Vector3.one * 8f;
        if (horizontal) quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        if (sharedMaterial == null)
        {
            Shader shader = Shader.Find("Shader Graphs/Mario 64 Painting");
            Assert.That(shader, Is.Not.Null, "Use the actual project painting shader in regression tests.");
            sharedMaterial = Track(new Material(shader));
            Assert.That(sharedMaterial.HasProperty(StrengthProperty), Is.True);
            sharedMaterial.SetFloat(StrengthProperty, 0.75f);
        }
        quad.GetComponent<Renderer>().sharedMaterial = sharedMaterial;
        Component painting = quad.AddComponent(paintingType);
        SetField(painting, "rippleTime", duration);
        if (passThrough) SetField(painting, "allowPlayerPassThrough", true);
        quad.SetActive(true);
        return painting;
    }

    private Component CreatePlayer(Vector3 position)
    {
        GameObject player = Track(new GameObject("Untagged regression test player"));
        player.SetActive(false);
        player.transform.position = position;
        Component mover = player.AddComponent(moverType);
        ((Behaviour)mover).enabled = false;
        player.SetActive(true);
        player.GetComponent<Rigidbody>().useGravity = false;
        return mover;
    }

    private void Play(Component painting, Vector3 worldPosition)
    {
        MethodInfo method = paintingType.GetMethod("PlayRipple", new[] { typeof(Vector3) });
        Assert.That(method, Is.Not.Null, "Painting must expose PlayRipple(Vector3 worldPosition).");
        method.Invoke(painting, new object[] { worldPosition });
    }

    private void CheckGround(Component mover)
    {
        moverType.GetMethod("CheckForGround").Invoke(mover, null);
    }

    private void PointerRay(Component painting, Ray ray)
    {
        MethodInfo method = paintingType.GetMethod("HandlePointerRay",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(painting, new object[] { ray });
    }

    private void SetField(Component target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "Missing serialized configuration: " + name);
        field.SetValue(target, value);
    }

    private static float Strength(Component painting)
    {
        Renderer renderer = painting.GetComponent<Renderer>();
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        return properties.HasFloat(StrengthProperty)
            ? properties.GetFloat(StrengthProperty)
            : renderer.sharedMaterial.GetFloat(StrengthProperty);
    }

    private static Vector3 Center(Component painting)
    {
        Renderer renderer = painting.GetComponent<Renderer>();
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        return properties.HasVector(CenterProperty)
            ? (Vector3)properties.GetVector(CenterProperty)
            : (Vector3)renderer.sharedMaterial.GetVector(CenterProperty);
    }

    private T Track<T>(T item) where T : UnityEngine.Object
    {
        createdObjects.Add(item);
        return item;
    }
}
