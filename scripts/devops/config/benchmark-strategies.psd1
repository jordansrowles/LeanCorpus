@{
    default = @{
        Description = '20K docs, --job short (development baseline)'
        DocCount = 0
        Job = @('--job', 'short')
    }
    fast = @{
        Description = '500 docs, --job dry (minimal smoke-test)'
        DocCount = 500
        Job = @('--job', 'dry')
    }
    'quick-compare' = @{
        Description = '1000 docs, --job short (quick comparison)'
        DocCount = 1000
        Job = @('--job', 'short')
    }
    intense = @{
        Description = '10K docs, default BDN job'
        DocCount = 10000
        Job = @('--job', 'default')
    }
    stress = @{
        Description = '50K docs, default BDN job'
        DocCount = 50000
        Job = @('--job', 'default')
    }
    exhaustive = @{
        Description = '100K docs, default BDN job (official reference)'
        DocCount = 100000
        Job = @('--job', 'default')
    }
}
