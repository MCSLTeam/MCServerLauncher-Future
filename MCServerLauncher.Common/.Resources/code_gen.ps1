if (Test-Path -Path ".venv") {
    Write-Host ":: venv is already exists"
    .\.venv\Scripts\Activate.ps1
    python ./proto_type.py
} else {
    Write-Host ":: creating venv..."
    python -m venv .venv
    .\.venv\Scripts\Activate.ps1
    pip install -r requirements.txt
    python ./proto_type.py
}
