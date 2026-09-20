// The shell reads its text through a single static provider, so a test that switches the language
// changes what every other test sees. Running this assembly serially is the price of being able to
// check translated text at all.
//
// The version of this in the WinForms suite adds that the tests each spin up an STA thread anyway.
// Nothing here does: no test in this assembly constructs a Control, which is what lets the whole
// project target net10.0 and run on Linux.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
