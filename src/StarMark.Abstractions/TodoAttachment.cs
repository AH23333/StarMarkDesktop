#nullable enable

namespace StarMark.Abstractions;

/// <summary>
/// 待办条目上挂着的一个文件附件（P-305）：名字给人看、路径给机器动。
/// <para>
/// 存放位置（<see cref="LocalItemState"/> 的 extra_json <c>attachments</c> 数组，每行
/// <c>{ "n": 名字, "p": 路径 }</c>）只记<b>最终形态</b>的那条路径：Linked 模式＝源文件原路径，
/// Copy 模式＝托管副本路径（%LOCALAPPDATA%\StarMark\attachments\todo\&lt;行 id&gt;\ 之下）。
/// <b>是不是托管副本不看记录、看路径</b>——落在该条目自己的托管目录里才算（见 TodoAttachmentPolicy.IsUnder），
/// 于是删除护栏只需要一个前缀判断，跨条目的托管副本与用户原件都动不着。
/// </para>
/// </summary>
public sealed record TodoAttachment(string Name, string Path);
