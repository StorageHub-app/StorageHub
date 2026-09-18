// The shell reads its text through a single static provider, so a test that renders the UI in
// another language changes what every other test sees. Running this assembly serially is the
// price of being able to check a translated layout at all; these are UI tests that each spin up
// their own STA thread, so the parallelism given up here is small.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
