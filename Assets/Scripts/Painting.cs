using AdvancedController;
using UnityEngine;

/// <summary>
/// SM64 风格画作涟漪控制器。
/// 管理涟漪触发、生命周期衰减及碰撞体切换，通过 MaterialPropertyBlock 
/// 将参数传递给 Shader Graph，避免材质实例化开销。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshRenderer), typeof(MeshFilter))]
public class Painting : MonoBehaviour
{
    // 缓存 Shader 属性 ID，避免每帧字符串查找（Shader.PropertyToID 有哈希开销）
    private static readonly int RippleCenter = Shader.PropertyToID("_RippleCenter");
    private static readonly int RippleStrength = Shader.PropertyToID("_RippleStrength");

    [SerializeField, Min(0f)] private float rippleTime = 1.5f;         // 涟漪完整衰减时长
    [SerializeField, Min(0f)] private float maxRippleStrength = 0.75f;  // 涟漪最大扭曲强度
    [Tooltip("Enable for a painting the player can walk through. Leave off for a painting used as a floor.")]
    [SerializeField] private bool allowPlayerPassThrough;               // 是否允许玩家穿过画作
    [Tooltip("Thickness of the pass-through trigger in the Quad's local space.")]
    [SerializeField, Min(0.01f)] private float triggerDepth = 0.1f;     // 穿透触发器的厚度

    private Renderer paintingRenderer;
    private MaterialPropertyBlock properties;  // MPB 避免 new Material()，支持 SRP Batcher
    private float remainingRippleTime;         // 当前涟漪剩余时间

    private void Awake()
    {
        paintingRenderer = GetComponent<Renderer>();
        properties = new MaterialPropertyBlock();

        if (allowPlayerPassThrough)
        {
            // ⚠️ 关键：MeshCollider 是凸面/网格碰撞，无法作为 Trigger 体积使用
            // 且平面 MeshCollider 会阻挡角色进入，必须禁用并替换为 BoxCollider
            MeshCollider meshCollider = GetComponent<MeshCollider>();
            if (meshCollider != null) meshCollider.enabled = false;

            // 基于 Mesh 实际包围盒创建 Trigger，确保触发区域精确匹配可视范围
            Bounds bounds = GetComponent<MeshFilter>().sharedMesh.bounds;
            BoxCollider trigger = gameObject.AddComponent<BoxCollider>();
            trigger.center = bounds.center;
            // Z 轴取 mesh 厚度与 triggerDepth 的较大值，防止过薄导致快速穿越时漏检
            trigger.size = new Vector3(bounds.size.x, bounds.size.y,
                Mathf.Max(bounds.size.z, Mathf.Max(triggerDepth, 0.01f)));
            trigger.isTrigger = true;
        }
    }

    // 启用/禁用时重置涟漪状态，防止恢复激活时残留上一帧的扭曲效果
    private void OnEnable()
    {
        remainingRippleTime = 0f;
        SetStrength(0f);
    }

    private void OnDisable()
    {
        remainingRippleTime = 0f;
        SetStrength(0f);
    }

    private void Update()
    {
        // ===== 涟漪衰减：线性插值从 maxRippleStrength → 0 =====
        if (remainingRippleTime > 0f)
        {
            remainingRippleTime = Mathf.Max(0f, remainingRippleTime - Time.deltaTime);
            // 线性衰减：剩余时间占比 × 最大强度
            // Shader Graph 中可对此值做 smoothstep 以获得更自然的缓动
            SetStrength(rippleTime > 0f ? maxRippleStrength * remainingRippleTime / rippleTime : 0f);
        }

        // ===== 鼠标点击测试（开发/调试用，生产环境应改为角色碰撞触发）=====
        if (!Input.GetMouseButtonDown(0)) return;

        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;

        HandlePointerRay(mainCamera.ScreenPointToRay(Input.mousePosition));
    }

    /// <summary>
    /// 处理射线检测：精确计算射线与画作平面的交点，而非 Trigger 表面
    /// </summary>
    private void HandlePointerRay(Ray ray)
    {
        // QueryTriggerInteraction.Collide 确保能命中 isTrigger=true 的 BoxCollider
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)) return;

        // 验证命中的确实是本画作（支持嵌套子物体）
        if (hit.collider.GetComponentInParent<Painting>() != this) return;

        // ⚠️ 关键修正：BoxCollider Trigger 有厚度，其 Raycast 命中点是 Trigger 表面
        // 而非画作可视平面。必须手动计算射线与 Mesh 所在平面的真实交点
        Bounds bounds = GetComponent<MeshFilter>().sharedMesh.bounds;
        Plane surface = new Plane(transform.forward, transform.TransformPoint(bounds.center));
        if (!surface.Raycast(ray, out float distance)) return;

        Vector3 point = ray.GetPoint(distance);
        Vector3 localPoint = transform.InverseTransformPoint(point);

        // 边界检查：确保交点在 Mesh 实际范围内（Trigger 可能比 Mesh 略大）
        if (localPoint.x < bounds.min.x || localPoint.x > bounds.max.x ||
            localPoint.y < bounds.min.y || localPoint.y > bounds.max.y) return;

        PlayRipple(point);
    }

    /// <summary>
    /// 启动或延续涟漪。世界空间坐标直接传入 Shader，由 GPU 完成变换
    /// </summary>
    public void PlayRipple(Vector3 worldPosition)
    {
        if (!isActiveAndEnabled) return;

        remainingRippleTime = Mathf.Max(0f, rippleTime);

        // MPB 工作流：Get → Modify → Set，不创建材质实例
        paintingRenderer.GetPropertyBlock(properties);
        properties.SetVector(RippleCenter, worldPosition);
        properties.SetFloat(RippleStrength, remainingRippleTime > 0f ? maxRippleStrength : 0f);
        paintingRenderer.SetPropertyBlock(properties);
    }

    // Trigger 进入和停留都触发涟漪，确保角色缓慢走入时涟漪持续跟随
    private void OnTriggerEnter(Collider other) => RippleFromPlayer(other);
    private void OnTriggerStay(Collider other) => RippleFromPlayer(other);

    private void RippleFromPlayer(Collider other)
    {
        // 项目约定：Player 无 Tag，通过 PlayerMover 组件识别（兼容子碰撞体）
        if (other.GetComponentInParent<PlayerMover>() == null) return;

        // 将角色包围盒中心投影到画作平面，作为涟漪原点
        PlayRipple(ProjectToSurface(other.bounds.center));
    }

    /// <summary>
    /// 将任意世界坐标投影到画作 Mesh 所在平面
    /// 保留 XY 分量，仅将 Z 对齐到 Mesh 中心深度
    /// </summary>
    private Vector3 ProjectToSurface(Vector3 worldPosition)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        localPosition.z = GetComponent<MeshFilter>().sharedMesh.bounds.center.z;
        return transform.TransformPoint(localPosition);
    }

    /// <summary>
    /// 仅更新涟漪强度（衰减阶段使用，不改变中心点）
    /// </summary>
    private void SetStrength(float strength)
    {
        paintingRenderer.GetPropertyBlock(properties);
        properties.SetFloat(RippleStrength, strength);
        paintingRenderer.SetPropertyBlock(properties);
    }
}