$in = [Console]::In.ReadToEnd()
if ($in -match 'UserSecrets') {
    [Console]::Error.WriteLine('Action denied: reading UserSecrets is forbidden')
    exit 2
}
exit 0
