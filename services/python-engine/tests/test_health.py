def test_health_returns_ok_with_expected_shape(client):
    response = client.get("/health")

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "ok"
    assert body["service"]
    assert body["version"]


def test_health_content_type_is_json(client):
    response = client.get("/health")

    assert response.headers["content-type"].startswith("application/json")
