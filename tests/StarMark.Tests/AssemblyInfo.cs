#nullable enable
using Xunit;

// DataChangeHub 是**进程级静态广播枢纽**：任何仓储写入（大量并行测试类都会触发）都会调用 Notify()，
// 遍历全进程共享的订阅者列表。DataChangeHubTests 用「订阅方计数器精确等于 0/1」断言广播语义，
// 但只要与其它并行测试类交错——某个外部仓储写入的 Notify() 在「本测试 Subscribe→Dispose 窗口内」
// 快照到本测试的存活委托并在其后调用它——计数就被污染，`Unsubscribe_StopsReceiving`(==0) 偶发失败
// （Debug 下 JIT 延长局部引用生命周期，弱委托更不易即时回收，窗口更宽）。这是全局静态 + 并行下精确计数
// 断言的固有冲突，非生产缺陷（生产为单进程、写入与退订均 lock 串行）。最省且可靠的处置：关闭测试程序集并行。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
