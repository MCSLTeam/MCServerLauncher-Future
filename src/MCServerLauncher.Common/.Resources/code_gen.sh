if [ -d ".venv" ]; then
    echo ":: venv is already exists"
    source .venv/bin/activate
    python ./proto_type.py
else
    echo ":: creating venv..."
    python -m venv .venv
    source .venv/bin/activate
    pip install -r requirements.txt
    python ./proto_type.py
fi